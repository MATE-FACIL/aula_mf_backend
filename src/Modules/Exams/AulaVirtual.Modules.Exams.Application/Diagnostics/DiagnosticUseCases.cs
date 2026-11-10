using System.Globalization;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Domain.Levels;
using AulaVirtual.Modules.Exams.Domain.Diagnostics;
using AulaVirtual.Modules.Exams.Domain.Practice;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Exams.Application.Diagnostics;

// Labels of the stage and level codes, taken from the Mathematics levels catalog (SCRUM-39) so both say the same.
internal static class DiagnosticLabels
{
    public static string Stage(string code) =>
        MathLevels.All.FirstOrDefault(level => level.StageCode == code)?.StageLabel ?? code;

    public static string Level(string code) =>
        MathLevels.All.FirstOrDefault(level => level.LevelCode == code)?.LevelLabel ?? code;

    public static LevelRef? LevelRef(string? code) => code is null ? null : new LevelRef(code, Level(code));

    public static string Status(DiagnosticStatus status) => status == DiagnosticStatus.Completed ? "completed" : "in_progress";

    /// <summary>The scored result; the answers must be in the order of the attempt's questions.</summary>
    public static DiagnosticResult Result(DiagnosticAttempt attempt, IReadOnlyList<DiagnosticAnswer> answers)
    {
        var score = DiagnosticScoring.Score(answers);
        return new DiagnosticResult(
            score.Topics.Select(line => new ScoreView(line.Key, line.Correct, line.Total, line.Percent)).ToArray(),
            score.Levels.Select(line => new ScoreView(Level(line.Key), line.Correct, line.Total, line.Percent)).ToArray(),
            LevelRef(attempt.SuggestedLevel ?? score.SuggestedLevel)!,
            LevelRef(attempt.ConfirmedLevel));
    }

    /// <summary>Answers in the order of the attempt's questions.</summary>
    public static IReadOnlyList<DiagnosticAnswer> InOrder(DiagnosticAttempt attempt, IEnumerable<DiagnosticAnswer> answers)
    {
        var order = attempt.QuestionIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        return answers.Where(answer => answer.AttemptId == attempt.Id).OrderBy(answer => order.GetValueOrDefault(answer.QuestionId, int.MaxValue)).ToArray();
    }

    internal static string Clean(string message)
    {
        var cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}

// The question bank of the entry diagnostic (SCRUM-47), one ordered list per stage, edited like a lesson's exercises:
// questions left out are archived, so the tests already started keep them.
public sealed class DiagnosticBankUseCases(IDiagnosticRepository repository, TimeProvider timeProvider)
{
    public const int MaxQuestionsPerStage = 60;
    private const string UnknownStage = "La etapa es «primary» o «secondary».";

    public async Task<DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>> GetAsync(string? stage,
        CancellationToken cancellationToken)
    {
        if (DiagnosticStages.Parse(stage) is not { } parsed)
            return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.NotFound(UnknownStage);
        return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Ok(
            ToAdminViews(await repository.GetActiveQuestionsAsync(parsed, cancellationToken)));
    }

    public async Task<DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>> ReplaceAsync(string? stage,
        IReadOnlyList<DiagnosticQuestionInput>? inputs, CancellationToken cancellationToken)
    {
        if (DiagnosticStages.Parse(stage) is not { } parsed)
            return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.NotFound(UnknownStage);
        inputs ??= [];
        if (inputs.Count > MaxQuestionsPerStage)
            return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Invalid(
                $"El diagnóstico de una etapa admite como máximo {MaxQuestionsPerStage} preguntas.");
        var ids = inputs.Where(input => input.Id is not null).Select(input => input.Id!.Value).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Invalid("Cada pregunta puede aparecer una sola vez.");

        var existing = (await repository.GetAllQuestionsAsync(parsed, cancellationToken)).ToDictionary(question => question.Id);
        if (ids.Any(id => !existing.ContainsKey(id)))
            return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Invalid("Una de las preguntas no pertenece a esta etapa.");

        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (!ExerciseKinds.TryParse(input.Kind, out var kind))
                return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Invalid(
                    $"Pregunta {index + 1}: el tipo es «multiple_choice» o «numeric».");
            var content = new ExerciseContent(kind, input.Prompt, input.Options, input.CorrectOption, input.CorrectNumber,
                input.Tolerance, input.Explanation);
            try
            {
                if (input.Id is { } id) existing[id].Update(input.Level, input.Topic, index + 1, content, now);
                else repository.Add(DiagnosticQuestion.Create(Guid.NewGuid(), parsed, input.Level, input.Topic, index + 1, content, now));
            }
            catch (ArgumentException exception)
            {
                return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Invalid(
                    $"Pregunta {index + 1}: {DiagnosticLabels.Clean(exception.Message)}");
            }
        }

        foreach (var question in existing.Values.Where(question => !ids.Contains(question.Id))) question.Archive(now);
        await repository.SaveChangesAsync(cancellationToken);
        return DiagnosticOutcome<IReadOnlyList<DiagnosticQuestionAdminView>>.Ok(
            ToAdminViews(await repository.GetActiveQuestionsAsync(parsed, cancellationToken)));
    }

    private static IReadOnlyList<DiagnosticQuestionAdminView> ToAdminViews(IReadOnlyList<DiagnosticQuestion> questions) =>
        questions.Select((question, index) => new DiagnosticQuestionAdminView(question.Id, index + 1, question.Level, question.Topic,
            ExerciseKinds.ToCode(question.Kind), question.Prompt, question.Options, question.CorrectOption, question.CorrectNumber,
            question.Tolerance, question.Explanation)).ToArray();
}

// The student's side (SCRUM-48/49): one diagnostic per stage, questions one at a time, no right/wrong while
// answering (it is a placement test, not practice), and the result per topic with a suggested level at the end.
// Fixed number of queries per request.
public sealed class StudentDiagnosticUseCases(IDiagnosticRepository repository, IRoleDirectory roles, TimeProvider timeProvider)
{
    private const string NotAvailable = "Este diagnóstico no está disponible para ti.";

    /// <summary>Both stages with the student's attempt, if any. Three queries.</summary>
    public async Task<IReadOnlyList<DiagnosticStageSummary>> OverviewAsync(Guid studentUserId, CancellationToken cancellationToken)
    {
        var attempts = await repository.GetAttemptsOfAsync(studentUserId, cancellationToken);
        var available = await repository.CountActiveQuestionsAsync(cancellationToken);
        var answers = attempts.Count == 0 ? [] : await repository.GetAnswersAsync(attempts.Select(a => a.Id).ToArray(), cancellationToken);

        return DiagnosticStages.All.Select(stage =>
        {
            var attempt = attempts.FirstOrDefault(item => item.Stage == stage);
            var own = attempt is null ? [] : DiagnosticLabels.InOrder(attempt, answers);
            return new DiagnosticStageSummary(stage, DiagnosticLabels.Stage(stage), available.GetValueOrDefault(stage) > 0,
                attempt?.Id, attempt is null ? null : DiagnosticLabels.Status(attempt.Status), own.Count,
                attempt?.QuestionIds.Length ?? 0,
                attempt is { Status: DiagnosticStatus.Completed } && own.Count > 0 ? DiagnosticLabels.Result(attempt, own) : null);
        }).ToArray();
    }

    /// <summary>Starts the stage's diagnostic, or returns the one under way. One per student and stage.</summary>
    public async Task<DiagnosticOutcome<StudentDiagnostic>> StartAsync(Guid studentUserId, string? stage, CancellationToken cancellationToken)
    {
        if (DiagnosticStages.Parse(stage) is not { } parsed)
            return DiagnosticOutcome<StudentDiagnostic>.Invalid("Elige primaria o secundaria.");
        var codes = await roles.GetRoleCodesByUserIdsAsync([studentUserId], cancellationToken);
        if (!(codes.GetValueOrDefault(studentUserId)?.Contains(Roles.Student) ?? false))
            return DiagnosticOutcome<StudentDiagnostic>.NotFound(NotAvailable);

        var existing = (await repository.GetAttemptsOfAsync(studentUserId, cancellationToken)).FirstOrDefault(item => item.Stage == parsed);
        if (existing is not null) return await ViewAsync(existing, cancellationToken);

        var questions = await repository.GetActiveQuestionsAsync(parsed, cancellationToken);
        if (questions.Count == 0)
            return DiagnosticOutcome<StudentDiagnostic>.Conflict("Todavía no hay preguntas para este diagnóstico. Avísale a tu profesor.");

        var attempt = DiagnosticAttempt.Start(Guid.NewGuid(), studentUserId, parsed, questions.Select(q => q.Id).ToArray(),
            timeProvider.GetUtcNow());
        repository.Add(attempt);
        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            // Started twice at the same time (two tabs): the other one won, continue with it.
            existing = (await repository.GetAttemptsOfAsync(studentUserId, cancellationToken)).FirstOrDefault(item => item.Stage == parsed);
            return existing is null
                ? DiagnosticOutcome<StudentDiagnostic>.Conflict("No pudimos empezar el diagnóstico. Inténtalo otra vez.")
                : await ViewAsync(existing, cancellationToken);
        }

        return DiagnosticOutcome<StudentDiagnostic>.Ok(ToStudentView(attempt, questions, []));
    }

    public async Task<DiagnosticOutcome<StudentDiagnostic>> GetAsync(Guid studentUserId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await repository.GetAttemptAsync(attemptId, cancellationToken);
        return attempt is null || attempt.StudentUserId != studentUserId
            ? DiagnosticOutcome<StudentDiagnostic>.NotFound(NotAvailable)
            : await ViewAsync(attempt, cancellationToken);
    }

    /// <summary>
    /// Saves one answer (<paramref name="skip"/>: «No lo sé», counts as wrong). Each question is answered once. The
    /// last answer finishes the diagnostic and returns its result.
    /// </summary>
    public async Task<DiagnosticOutcome<DiagnosticAnswerResult>> AnswerAsync(Guid studentUserId, Guid attemptId, Guid questionId,
        string? answer, bool skip, CancellationToken cancellationToken)
    {
        var attempt = await repository.GetAttemptAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.StudentUserId != studentUserId || !attempt.QuestionIds.Contains(questionId))
            return DiagnosticOutcome<DiagnosticAnswerResult>.NotFound(NotAvailable);
        if (attempt.Status == DiagnosticStatus.Completed)
            return DiagnosticOutcome<DiagnosticAnswerResult>.Conflict("Ya terminaste este diagnóstico.");

        var question = (await repository.GetQuestionsAsync([questionId], cancellationToken)).Single();
        bool isCorrect;
        string? text = null;
        if (skip) isCorrect = false;
        else
        {
            if (question.Check(answer) is not { } checkedAnswer)
                return DiagnosticOutcome<DiagnosticAnswerResult>.Invalid(question.Kind == ExerciseKind.MultipleChoice
                    ? "Elige una de las opciones."
                    : "Escribe un número, por ejemplo 3,5.");
            isCorrect = checkedAnswer;
            text = answer;
        }

        var now = timeProvider.GetUtcNow();
        repository.Add(DiagnosticAnswer.Record(Guid.NewGuid(), attempt, question, text, isCorrect, now));
        if (!await repository.TrySaveChangesAsync(cancellationToken))
            return DiagnosticOutcome<DiagnosticAnswerResult>.Conflict("Ya respondiste esta pregunta.");

        var answers = DiagnosticLabels.InOrder(attempt, await repository.GetAnswersAsync([attempt.Id], cancellationToken));
        var total = attempt.QuestionIds.Length;
        if (answers.Count < total) return DiagnosticOutcome<DiagnosticAnswerResult>.Ok(new DiagnosticAnswerResult(answers.Count, total, false, null));

        // The tracked attempt was cleared only if the save failed; here it is still tracked.
        attempt.Complete(DiagnosticScoring.Score(answers).SuggestedLevel, now);
        await repository.SaveChangesAsync(cancellationToken);
        return DiagnosticOutcome<DiagnosticAnswerResult>.Ok(new DiagnosticAnswerResult(total, total, true,
            DiagnosticLabels.Result(attempt, answers)));
    }

    /// <summary>Questions (1) + answers (1).</summary>
    private async Task<DiagnosticOutcome<StudentDiagnostic>> ViewAsync(DiagnosticAttempt attempt, CancellationToken cancellationToken)
    {
        var questions = await repository.GetQuestionsAsync(attempt.QuestionIds, cancellationToken);
        var answers = DiagnosticLabels.InOrder(attempt, await repository.GetAnswersAsync([attempt.Id], cancellationToken));
        return DiagnosticOutcome<StudentDiagnostic>.Ok(ToStudentView(attempt, questions, answers));
    }

    private static StudentDiagnostic ToStudentView(DiagnosticAttempt attempt, IReadOnlyList<DiagnosticQuestion> questions,
        IReadOnlyList<DiagnosticAnswer> answers)
    {
        var byId = questions.ToDictionary(question => question.Id);
        var answered = answers.Select(answer => answer.QuestionId).ToHashSet();
        var views = attempt.QuestionIds.Where(byId.ContainsKey).Select((id, index) =>
        {
            var question = byId[id];
            return new DiagnosticQuestionStudentView(id, index + 1, question.Topic, ExerciseKinds.ToCode(question.Kind), question.Prompt,
                question.Options, answered.Contains(id));
        }).ToArray();
        return new StudentDiagnostic(attempt.Id, attempt.Stage, DiagnosticLabels.Stage(attempt.Stage), DiagnosticLabels.Status(attempt.Status),
            answers.Count, attempt.QuestionIds.Length, views,
            attempt.Status == DiagnosticStatus.Completed && answers.Count > 0 ? DiagnosticLabels.Result(attempt, answers) : null);
    }
}

// The teacher's side (SCRUM-48): the diagnostics, the detail of each answer, and the confirmation of the level.
// Requires `diagnostics.review` (administrators and teachers by default).
public sealed class DiagnosticReviewUseCases(IDiagnosticRepository repository, IUserDirectory users, TimeProvider timeProvider)
{
    public const int MaxPageSize = 100;
    private const string NotFound = "No encontramos ese diagnóstico.";

    /// <summary>Page (2) + answers per attempt (1) + names (1), whatever the page size.</summary>
    public async Task<DiagnosticPage> ListAsync(DiagnosticFilter filter, string? stage, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var (items, total) = await repository.SearchAttemptsAsync(filter, DiagnosticStages.Parse(stage), page, pageSize, cancellationToken);
        if (items.Count == 0) return new DiagnosticPage([], page, pageSize, total);

        var counts = await repository.CountAnswersAsync(items.Select(item => item.Id).ToArray(), cancellationToken);
        var names = await NamesAsync(items.Select(item => item.StudentUserId), cancellationToken);
        return new DiagnosticPage(items.Select(item => Summary(item, counts.GetValueOrDefault(item.Id), names)).ToArray(), page, pageSize, total);
    }

    /// <summary>Attempt (1) + questions (1) + answers (1) + names (1).</summary>
    public async Task<DiagnosticOutcome<DiagnosticReview>> GetAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await repository.GetAttemptAsync(attemptId, cancellationToken);
        if (attempt is null) return DiagnosticOutcome<DiagnosticReview>.NotFound(NotFound);
        return DiagnosticOutcome<DiagnosticReview>.Ok(await ReviewAsync(attempt, cancellationToken));
    }

    public async Task<DiagnosticOutcome<DiagnosticReview>> ConfirmAsync(Guid attemptId, string? level, Guid reviewerUserId,
        CancellationToken cancellationToken)
    {
        if (DiagnosticLevels.Parse(level) is not { } parsed)
            return DiagnosticOutcome<DiagnosticReview>.Invalid("El nivel es «prebasic», «basic», «intermediate» o «advanced».");
        var attempt = await repository.GetAttemptAsync(attemptId, cancellationToken);
        if (attempt is null) return DiagnosticOutcome<DiagnosticReview>.NotFound(NotFound);
        if (attempt.Status != DiagnosticStatus.Completed)
            return DiagnosticOutcome<DiagnosticReview>.Conflict("El alumno todavía no termina el diagnóstico.");

        attempt.Confirm(parsed, reviewerUserId, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return DiagnosticOutcome<DiagnosticReview>.Ok(await ReviewAsync(attempt, cancellationToken));
    }

    private async Task<DiagnosticReview> ReviewAsync(DiagnosticAttempt attempt, CancellationToken cancellationToken)
    {
        var questions = (await repository.GetQuestionsAsync(attempt.QuestionIds, cancellationToken)).ToDictionary(q => q.Id);
        var answers = DiagnosticLabels.InOrder(attempt, await repository.GetAnswersAsync([attempt.Id], cancellationToken));
        var names = await NamesAsync(attempt.ConfirmedByUserId is { } reviewer ? [attempt.StudentUserId, reviewer] : [attempt.StudentUserId],
            cancellationToken);

        var byQuestion = answers.ToDictionary(answer => answer.QuestionId);
        var lines = attempt.QuestionIds.Where(questions.ContainsKey).Select((id, index) =>
        {
            var question = questions[id];
            var own = byQuestion.GetValueOrDefault(id);
            return new DiagnosticAnswerReview(index + 1, own?.Topic ?? question.Topic, DiagnosticLabels.Level(own?.Level ?? question.Level),
                question.Prompt, own is not null, AnswerText(question, own?.Answer), AnswerText(question, CorrectCode(question))!,
                own?.IsCorrect ?? false, question.Explanation);
        }).ToArray();

        return new DiagnosticReview(Summary(attempt, answers.Count, names),
            attempt.Status == DiagnosticStatus.Completed && answers.Count > 0 ? DiagnosticLabels.Result(attempt, answers) : null,
            lines, attempt.ConfirmedByUserId is { } by ? names.GetValueOrDefault(by) : null, attempt.ConfirmedAt);
    }

    private static string CorrectCode(DiagnosticQuestion question) => question.Kind == ExerciseKind.MultipleChoice
        ? question.CorrectOption!.Value.ToString(CultureInfo.InvariantCulture)
        : question.CorrectNumber!.Value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>The option's text for multiple choice; the number as typed otherwise.</summary>
    private static string? AnswerText(DiagnosticQuestion question, string? answer)
    {
        if (answer is null) return null;
        if (question.Kind != ExerciseKind.MultipleChoice) return answer;
        return int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var option) && option < question.Options.Length
            ? question.Options[option]
            : answer;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        return (await users.GetManyAsync(ids, cancellationToken)).ToDictionary(user => user.Id, user => user.FullName);
    }

    private static DiagnosticListItem Summary(DiagnosticAttempt attempt, int answered, IReadOnlyDictionary<Guid, string> names) =>
        new(attempt.Id, attempt.StudentUserId, names.GetValueOrDefault(attempt.StudentUserId) ?? "Alumno sin nombre", attempt.Stage,
            DiagnosticLabels.Stage(attempt.Stage), DiagnosticLabels.Status(attempt.Status), answered, attempt.QuestionIds.Length,
            attempt.StartedAt, attempt.CompletedAt, DiagnosticLabels.LevelRef(attempt.SuggestedLevel), DiagnosticLabels.LevelRef(attempt.ConfirmedLevel));
}
