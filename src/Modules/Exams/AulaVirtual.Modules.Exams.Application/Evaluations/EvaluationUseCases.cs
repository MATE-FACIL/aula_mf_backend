using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Exams.Domain.Evaluations;
using AulaVirtual.Modules.Exams.Domain.Practice;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Exams.Application.Evaluations;

/// <summary>Pure scoring helpers, so results by competency are computed in one place and tested without a database.</summary>
public static class EvaluationScoring
{
    /// <summary>Correct/total per competency, in the order of the catalog (competencies with no questions are left out).</summary>
    public static IReadOnlyList<CompetencyScore> ByCompetency(IEnumerable<(string Competency, bool IsCorrect)> answers) =>
        answers.GroupBy(answer => answer.Competency)
            .Select(group => new CompetencyScore(group.Key, Competencies.LabelOf(group.Key), group.Count(a => a.IsCorrect), group.Count()))
            .OrderBy(score => Competencies.All.Keys.ToList().IndexOf(score.Competency) is var index && index < 0 ? int.MaxValue : index)
            .ThenBy(score => score.Competency, StringComparer.Ordinal)
            .ToArray();

    public static string StatusOf(Evaluation evaluation, EvaluationAttempt? attempt, DateTimeOffset now)
    {
        if (attempt is { IsSubmitted: true }) return "graded";
        if (attempt is not null) return now <= attempt.Deadline + EvaluationAttempt.Grace ? "in_progress" : "missed";
        return evaluation.WindowAt(now) switch
        {
            WindowState.Upcoming => "upcoming",
            WindowState.Open => "open",
            _ => "missed"
        };
    }
}

// The teacher's side of evaluations (SCRUM-62): a teacher manages the evaluations of the groups they are assigned to.
// A group that is not theirs, and an evaluation of such a group, answer "not found". Questions can change only while
// nobody has started the evaluation, so a student's score is always about the questions they were asked.
public sealed class TeacherEvaluationUseCases(
    IEvaluationRepository repository,
    ITeachingDirectory teaching,
    IEnrollmentRoster roster,
    IUserDirectory users,
    TimeProvider timeProvider)
{
    private const string NotYours = "No tienes asignado ese grupo.";
    private const string EvaluationNotFound = "No encontramos esa evaluación en tus grupos.";

    public async Task<EvaluationOutcome<IReadOnlyList<EvaluationSummaryView>>> ListAsync(Guid teacherUserId, Guid offeringId,
        CancellationToken cancellationToken)
    {
        if (await GroupAsync(teacherUserId, offeringId, cancellationToken) is null)
            return EvaluationOutcome<IReadOnlyList<EvaluationSummaryView>>.NotFound(NotYours);
        var evaluations = await repository.ListByOfferingAsync(offeringId, cancellationToken);
        return EvaluationOutcome<IReadOnlyList<EvaluationSummaryView>>.Ok(await SummariesAsync(evaluations, offeringId, cancellationToken));
    }

    public async Task<EvaluationOutcome<EvaluationDetailView>> CreateAsync(Guid teacherUserId, Guid offeringId, EvaluationInput input,
        CancellationToken cancellationToken)
    {
        var group = await GroupAsync(teacherUserId, offeringId, cancellationToken);
        if (group is null) return EvaluationOutcome<EvaluationDetailView>.NotFound(NotYours);

        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        try
        {
            var questions = BuildQuestions(id, input.Questions, input.IsPublished);
            if (questions.Problem is not null) return EvaluationOutcome<EvaluationDetailView>.Invalid(questions.Problem);
            var evaluation = Evaluation.Create(id, offeringId, group.CourseId, teacherUserId, ParseKind(input.Kind), input.Title,
                input.OpensAt ?? default, input.ClosesAt ?? default, input.DurationMinutes ?? 0, input.IsPublished, now);
            repository.Add(evaluation);
            foreach (var question in questions.Items) repository.Add(question);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return EvaluationOutcome<EvaluationDetailView>.Invalid(Clean(exception.Message));
        }

        return await DetailAsync(teacherUserId, id, cancellationToken);
    }

    public async Task<EvaluationOutcome<EvaluationDetailView>> GetAsync(Guid teacherUserId, Guid evaluationId, CancellationToken cancellationToken) =>
        await DetailAsync(teacherUserId, evaluationId, cancellationToken);

    public async Task<EvaluationOutcome<EvaluationDetailView>> UpdateAsync(Guid teacherUserId, Guid evaluationId, EvaluationInput input,
        CancellationToken cancellationToken)
    {
        var evaluation = await OwnedAsync(teacherUserId, evaluationId, cancellationToken);
        if (evaluation is null) return EvaluationOutcome<EvaluationDetailView>.NotFound(EvaluationNotFound);

        var counts = (await repository.CountAsync([evaluationId], cancellationToken)).GetValueOrDefault(evaluationId);
        var started = counts is { Started: > 0 };
        try
        {
            var current = await repository.GetQuestionsAsync(evaluationId, cancellationToken);
            Built? replacement = null;
            if (started)
            {
                if (input.Questions is not null && !SameQuestions(current, input.Questions))
                    return EvaluationOutcome<EvaluationDetailView>.Conflict("Ya hay alumnos que empezaron esta evaluación: sus preguntas no se pueden cambiar.");
                if (!input.IsPublished)
                    return EvaluationOutcome<EvaluationDetailView>.Conflict("Ya hay alumnos que empezaron esta evaluación: no se puede volver a borrador.");
            }
            else
            {
                replacement = BuildQuestions(evaluationId, input.Questions ?? [], input.IsPublished);
                if (replacement.Problem is not null) return EvaluationOutcome<EvaluationDetailView>.Invalid(replacement.Problem);
            }

            evaluation.Update(ParseKind(input.Kind), input.Title, input.OpensAt ?? default, input.ClosesAt ?? default,
                input.DurationMinutes ?? 0, input.IsPublished, timeProvider.GetUtcNow());
            if (replacement is not null)
            {
                repository.Remove(current);
                foreach (var question in replacement.Items) repository.Add(question);
            }
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return EvaluationOutcome<EvaluationDetailView>.Invalid(Clean(exception.Message));
        }

        return await DetailAsync(teacherUserId, evaluationId, cancellationToken);
    }

    public async Task<EvaluationOutcome<bool>> DeleteAsync(Guid teacherUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await OwnedAsync(teacherUserId, evaluationId, cancellationToken);
        if (evaluation is null) return EvaluationOutcome<bool>.NotFound(EvaluationNotFound);
        var counts = (await repository.CountAsync([evaluationId], cancellationToken)).GetValueOrDefault(evaluationId);
        if (counts is { Started: > 0 })
            return EvaluationOutcome<bool>.Conflict("Ya hay alumnos que empezaron esta evaluación: no se puede borrar.");
        repository.Remove(await repository.GetQuestionsAsync(evaluationId, cancellationToken));
        repository.Remove(evaluation);
        await repository.SaveChangesAsync(cancellationToken);
        return EvaluationOutcome<bool>.Ok(true);
    }

    /// <summary>Every student of the group with their state and competencies, plus the group average. Fixed number of queries.</summary>
    public async Task<EvaluationOutcome<EvaluationResultsView>> ResultsAsync(Guid teacherUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await OwnedAsync(teacherUserId, evaluationId, cancellationToken);
        if (evaluation is null) return EvaluationOutcome<EvaluationResultsView>.NotFound(EvaluationNotFound);

        var summary = (await SummariesAsync([evaluation], evaluation.OfferingId, cancellationToken))[0];
        var attempts = await repository.GetAttemptsAsync(evaluationId, cancellationToken);
        var answers = await repository.GetAnswersAsync(attempts.Where(a => a.IsSubmitted).Select(a => a.Id).ToArray(), cancellationToken);
        var students = await roster.GetActiveStudentIdsAsync(evaluation.OfferingId, cancellationToken);
        // A student who sent it and then left the group still has their result.
        var everyone = students.Concat(attempts.Select(attempt => attempt.StudentUserId)).Distinct().ToArray();
        var names = (await users.GetManyAsync(everyone, cancellationToken)).ToDictionary(user => user.Id, user => user.FullName);

        var attemptOf = attempts.ToDictionary(attempt => attempt.StudentUserId);
        var answersOf = answers.GroupBy(answer => answer.AttemptId).ToDictionary(group => group.Key, group => group.ToArray());
        var rows = everyone.Select(studentId =>
        {
            attemptOf.TryGetValue(studentId, out var attempt);
            var name = names.GetValueOrDefault(studentId) ?? "(cuenta eliminada)";
            if (attempt is not { IsSubmitted: true })
                return new StudentEvaluationResultView(studentId, name, attempt is null ? "not_started" : "in_progress", null, null, null, false, []);
            var mine = answersOf.GetValueOrDefault(attempt.Id) ?? [];
            return new StudentEvaluationResultView(studentId, name, "graded", attempt.Score, EvaluationAttempt.Percent(attempt.CorrectCount, attempt.QuestionCount),
                attempt.SubmittedAt, attempt.IsLate, EvaluationScoring.ByCompetency(mine.Select(a => (a.Competency, a.IsCorrect))));
        }).OrderBy(row => row.FullName, StringComparer.CurrentCultureIgnoreCase).ToArray();

        var graded = rows.Where(row => row.Score is not null).ToArray();
        decimal? average = graded.Length == 0 ? null : Math.Round(graded.Average(row => row.Score!.Value), 1, MidpointRounding.AwayFromZero);
        var overall = EvaluationScoring.ByCompetency(answers.Select(answer => (answer.Competency, answer.IsCorrect)));
        return EvaluationOutcome<EvaluationResultsView>.Ok(new EvaluationResultsView(summary, average, overall, rows));
    }

    // ─── helpers ───────────────────────────────────────────────────────

    private async Task<TeacherGroup?> GroupAsync(Guid teacherUserId, Guid offeringId, CancellationToken cancellationToken) =>
        (await teaching.GetGroupsAsync(teacherUserId, cancellationToken)).FirstOrDefault(group => group.OfferingId == offeringId);

    private async Task<Evaluation?> OwnedAsync(Guid teacherUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await repository.GetAsync(evaluationId, cancellationToken);
        return evaluation is not null && await GroupAsync(teacherUserId, evaluation.OfferingId, cancellationToken) is not null ? evaluation : null;
    }

    private async Task<EvaluationOutcome<EvaluationDetailView>> DetailAsync(Guid teacherUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await OwnedAsync(teacherUserId, evaluationId, cancellationToken);
        if (evaluation is null) return EvaluationOutcome<EvaluationDetailView>.NotFound(EvaluationNotFound);
        var summary = (await SummariesAsync([evaluation], evaluation.OfferingId, cancellationToken))[0];
        var questions = (await repository.GetQuestionsAsync(evaluationId, cancellationToken)).Select(ToAdminView).ToArray();
        return EvaluationOutcome<EvaluationDetailView>.Ok(new EvaluationDetailView(summary, questions));
    }

    /// <summary>Counts in two grouped queries and the roster size in one, whatever the number of evaluations.</summary>
    private async Task<IReadOnlyList<EvaluationSummaryView>> SummariesAsync(IReadOnlyList<Evaluation> evaluations, Guid offeringId,
        CancellationToken cancellationToken)
    {
        if (evaluations.Count == 0) return [];
        var counts = await repository.CountAsync(evaluations.Select(evaluation => evaluation.Id).ToArray(), cancellationToken);
        var enrolled = (await roster.CountActiveAsync([offeringId], cancellationToken)).GetValueOrDefault(offeringId);
        return evaluations.Select(evaluation =>
        {
            var count = counts.GetValueOrDefault(evaluation.Id) ?? new EvaluationCounts(0, 0, 0);
            return new EvaluationSummaryView(evaluation.Id, evaluation.OfferingId, evaluation.CourseId, EvaluationKinds.ToCode(evaluation.Kind),
                evaluation.Title, evaluation.OpensAt, evaluation.ClosesAt, evaluation.DurationMinutes, evaluation.IsPublished,
                count.Questions, count.Started, count.Submitted, enrolled);
        }).ToArray();
    }

    private static EvaluationQuestionAdminView ToAdminView(EvaluationQuestion question) =>
        new(question.Id, question.Position, question.Competency, Competencies.LabelOf(question.Competency), ExerciseKinds.ToCode(question.Kind),
            question.Prompt, question.Options, question.CorrectOption, question.CorrectNumber, question.Tolerance, question.Explanation);

    private sealed record Built(IReadOnlyList<EvaluationQuestion> Items, string? Problem);

    private static Built BuildQuestions(Guid evaluationId, IReadOnlyList<EvaluationQuestionInput>? inputs, bool publishing)
    {
        inputs ??= [];
        if (inputs.Count > Evaluation.MaxQuestions) return new Built([], $"Una evaluación admite como máximo {Evaluation.MaxQuestions} preguntas.");
        if (publishing && inputs.Count == 0) return new Built([], "Para publicarla, la evaluación necesita al menos una pregunta.");
        var items = new List<EvaluationQuestion>(inputs.Count);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (!ExerciseKinds.TryParse(input.Kind, out var kind))
                return new Built([], $"Pregunta {index + 1}: el tipo es «multiple_choice» o «numeric».");
            try
            {
                items.Add(EvaluationQuestion.Create(Guid.NewGuid(), evaluationId, index + 1, new EvaluationQuestionContent(input.Competency ?? string.Empty,
                    new ExerciseContent(kind, input.Prompt ?? string.Empty, input.Options, input.CorrectOption, input.CorrectNumber, input.Tolerance, input.Explanation))));
            }
            catch (ArgumentException exception)
            {
                return new Built([], $"Pregunta {index + 1}: {Clean(exception.Message)}");
            }
        }
        return new Built(items, null);
    }

    /// <summary>Whether the incoming questions are exactly the saved ones (an edit that does not touch them).</summary>
    private static bool SameQuestions(IReadOnlyList<EvaluationQuestion> current, IReadOnlyList<EvaluationQuestionInput> incoming)
    {
        if (current.Count != incoming.Count) return false;
        for (var index = 0; index < current.Count; index++)
        {
            var saved = current[index];
            var input = incoming[index];
            if (input.Competency != saved.Competency || !ExerciseKinds.TryParse(input.Kind, out var kind) || kind != saved.Kind) return false;
            if ((input.Prompt ?? string.Empty).Trim() != saved.Prompt) return false;
            if (kind == ExerciseKind.MultipleChoice
                ? input.CorrectOption != saved.CorrectOption || !(input.Options ?? []).Select(option => option.Trim()).SequenceEqual(saved.Options)
                : input.CorrectNumber != saved.CorrectNumber || (input.Tolerance ?? 0) != saved.Tolerance) return false;
            if ((input.Explanation?.Trim() is { Length: > 0 } text ? text : null) != saved.Explanation) return false;
        }
        return true;
    }

    private static EvaluationKind ParseKind(string? code) =>
        EvaluationKinds.TryParse(code, out var kind) ? kind : throw new ArgumentException("El tipo es «weekly», «midterm» o «final».", nameof(code));

    private static string Clean(string message)
    {
        var cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}

// The student's side (SCRUM-62): only PUBLISHED evaluations of the groups where the student has an ACTIVE enrollment.
// Start and submit are decided with the server's clock; the browser's clock only draws the countdown.
public sealed class StudentEvaluationUseCases(
    IEvaluationRepository repository,
    IStudentCourseAccess access,
    IOfferingDirectory offerings,
    TimeProvider timeProvider)
{
    private const string NotFound = "Esta evaluación no está disponible para ti.";

    /// <summary>Active enrollments (1), groups (1), evaluations (1), attempts (1) and counts (1): fixed cost.</summary>
    public async Task<IReadOnlyList<StudentEvaluationView>> ListAsync(Guid studentUserId, CancellationToken cancellationToken)
    {
        var offeringIds = await access.GetActiveOfferingIdsAsync(studentUserId, cancellationToken);
        if (offeringIds.Count == 0) return [];
        var evaluations = await repository.ListPublishedAsync(offeringIds, cancellationToken);
        if (evaluations.Count == 0) return [];

        var ids = evaluations.Select(evaluation => evaluation.Id).ToArray();
        var groups = (await offerings.GetManyAsync(offeringIds, cancellationToken)).ToDictionary(group => group.Id);
        var attempts = (await repository.GetAttemptsOfStudentAsync(studentUserId, ids, cancellationToken)).ToDictionary(attempt => attempt.EvaluationId);
        var counts = await repository.CountAsync(ids, cancellationToken);
        var now = timeProvider.GetUtcNow();
        return evaluations.Select(evaluation =>
        {
            attempts.TryGetValue(evaluation.Id, out var attempt);
            groups.TryGetValue(evaluation.OfferingId, out var group);
            return new StudentEvaluationView(evaluation.Id, EvaluationKinds.ToCode(evaluation.Kind), evaluation.Title, group?.CourseTitle ?? string.Empty,
                group?.Name ?? string.Empty, evaluation.OpensAt, evaluation.ClosesAt, evaluation.DurationMinutes,
                counts.GetValueOrDefault(evaluation.Id)?.Questions ?? 0, EvaluationScoring.StatusOf(evaluation, attempt, now),
                attempt is { IsSubmitted: true } ? attempt.Score : null,
                attempt is { IsSubmitted: true } ? EvaluationAttempt.Percent(attempt.CorrectCount, attempt.QuestionCount) : null, attempt?.Id);
        }).ToArray();
    }

    /// <summary>Starts the attempt, or returns the one already running (a second tab or a reload never resets the clock).</summary>
    public async Task<EvaluationOutcome<StartedAttemptView>> StartAsync(Guid studentUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await AccessibleAsync(studentUserId, evaluationId, cancellationToken);
        if (evaluation is null) return EvaluationOutcome<StartedAttemptView>.NotFound(NotFound);
        var now = timeProvider.GetUtcNow();

        var attempt = await repository.GetAttemptAsync(evaluationId, studentUserId, cancellationToken);
        if (attempt is { IsSubmitted: true }) return EvaluationOutcome<StartedAttemptView>.Conflict("Ya rendiste esta evaluación.");
        if (attempt is not null && now > attempt.Deadline + EvaluationAttempt.Grace)
            return EvaluationOutcome<StartedAttemptView>.Conflict("Se acabó el tiempo de esta evaluación.");
        if (attempt is null)
        {
            switch (evaluation.WindowAt(now))
            {
                case WindowState.Upcoming: return EvaluationOutcome<StartedAttemptView>.Conflict("Esta evaluación todavía no abre.");
                case WindowState.Closed: return EvaluationOutcome<StartedAttemptView>.Conflict("El plazo de esta evaluación terminó.");
            }
            var created = EvaluationAttempt.Start(Guid.NewGuid(), evaluation, studentUserId, now);
            attempt = await repository.TryAddAttemptAsync(created, cancellationToken)
                ? created
                : await repository.GetAttemptAsync(evaluationId, studentUserId, cancellationToken) ?? created;
        }

        var questions = (await repository.GetQuestionsAsync(evaluationId, cancellationToken))
            .Select(question => new EvaluationQuestionStudentView(question.Id, question.Position, ExerciseKinds.ToCode(question.Kind),
                question.Competency, question.Prompt, question.Options)).ToArray();
        var seconds = (int)Math.Max(0, (attempt.Deadline - attempt.StartedAt).TotalSeconds);
        return EvaluationOutcome<StartedAttemptView>.Ok(new StartedAttemptView(attempt.Id, evaluationId, evaluation.Title, seconds,
            attempt.StartedAt, attempt.Deadline, now, questions));
    }

    /// <summary>Corrects every answer on the server and saves the score. A second submit is a conflict, never a second grade.</summary>
    public async Task<EvaluationOutcome<SubmittedResultView>> SubmitAsync(Guid studentUserId, Guid attemptId,
        IReadOnlyDictionary<string, string?>? answers, CancellationToken cancellationToken)
    {
        var attempt = await repository.GetAttemptByIdAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.StudentUserId != studentUserId) return EvaluationOutcome<SubmittedResultView>.NotFound(NotFound);
        if (attempt.IsSubmitted) return EvaluationOutcome<SubmittedResultView>.Conflict("Esta evaluación ya se envió.");
        var now = timeProvider.GetUtcNow();
        if (!attempt.CanSubmitAt(now)) return EvaluationOutcome<SubmittedResultView>.Conflict("Se acabó el tiempo: ya no se puede enviar esta evaluación.");

        var questions = await repository.GetQuestionsAsync(attempt.EvaluationId, cancellationToken);
        var given = answers ?? new Dictionary<string, string?>();
        var graded = questions.Select(question =>
        {
            given.TryGetValue(question.Id.ToString(), out var answer);
            return (Question: question, Answer: answer, IsCorrect: question.Check(answer) == true);
        }).ToArray();

        foreach (var item in graded)
            repository.Add(EvaluationAnswer.Record(Guid.NewGuid(), attempt.Id, item.Question, item.Answer, item.IsCorrect));
        attempt.Submit(graded.Count(item => item.IsCorrect), graded.Length, now);
        if (!await repository.TrySaveAsync(cancellationToken))
            return EvaluationOutcome<SubmittedResultView>.Conflict("Esta evaluación ya se envió.");

        return EvaluationOutcome<SubmittedResultView>.Ok(ToResult(attempt, graded.Select(item => (item.Question, item.IsCorrect)).ToArray()));
    }

    /// <summary>The result of an evaluation already sent; not found while it is pending or if it is not the student's.</summary>
    public async Task<EvaluationOutcome<SubmittedResultView>> ResultAsync(Guid studentUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var attempt = await repository.GetAttemptAsync(evaluationId, studentUserId, cancellationToken);
        if (attempt is not { IsSubmitted: true }) return EvaluationOutcome<SubmittedResultView>.NotFound("Todavía no hay resultado de esta evaluación.");
        var questions = await repository.GetQuestionsAsync(evaluationId, cancellationToken);
        var answers = (await repository.GetAnswersAsync([attempt.Id], cancellationToken)).ToDictionary(answer => answer.QuestionId);
        return EvaluationOutcome<SubmittedResultView>.Ok(ToResult(attempt, questions.Select(question => (question, answers.TryGetValue(question.Id, out var a) && a.IsCorrect)).ToArray()));
    }

    private async Task<Evaluation?> AccessibleAsync(Guid studentUserId, Guid evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await repository.GetAsync(evaluationId, cancellationToken);
        if (evaluation is not { IsPublished: true }) return null;
        var offeringIds = await access.GetActiveOfferingIdsAsync(studentUserId, cancellationToken);
        return offeringIds.Contains(evaluation.OfferingId) ? evaluation : null;
    }

    private static SubmittedResultView ToResult(EvaluationAttempt attempt, IReadOnlyList<(EvaluationQuestion Question, bool IsCorrect)> graded) =>
        new(attempt.EvaluationId, attempt.Score ?? 0, EvaluationAttempt.Percent(attempt.CorrectCount, attempt.QuestionCount), attempt.CorrectCount,
            attempt.QuestionCount, attempt.IsLate, EvaluationScoring.ByCompetency(graded.Select(item => (item.Question.Competency, item.IsCorrect))),
            graded.Select(item => new QuestionFeedback(item.Question.Id, item.IsCorrect, item.Question.Explanation)).ToArray());
}
