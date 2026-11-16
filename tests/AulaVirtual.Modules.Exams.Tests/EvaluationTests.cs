using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Exams.Application.Evaluations;
using AulaVirtual.Modules.Exams.Domain.Evaluations;
using AulaVirtual.Modules.Exams.Domain.Practice;

namespace AulaVirtual.Modules.Exams.Tests;

public sealed class EvaluationDomainTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static Evaluation Make(int minutes = 30, int opensInMin = 0, int openForMin = 60) =>
        Evaluation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvaluationKind.Weekly, "  Semanal 1 ",
            Noon.AddMinutes(opensInMin), Noon.AddMinutes(opensInMin + openForMin), minutes, true, Noon);

    [Fact]
    public void Create_TrimsTheTitle_AndRejectsABadWindowOrDuration()
    {
        Assert.Equal("Semanal 1", Make().Title);
        Assert.Throws<ArgumentException>(() => Make(openForMin: 0));
        Assert.Throws<ArgumentException>(() => Make(minutes: 4));
        Assert.Throws<ArgumentException>(() => Make(minutes: 181));
        Assert.Throws<ArgumentException>(() => Evaluation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            EvaluationKind.Final, " ", Noon, Noon.AddHours(1), 30, false, Noon));
    }

    [Fact]
    public void Window_IsDecidedFromTheServerClock_AtBothEdges()
    {
        var evaluation = Make(opensInMin: 10);
        Assert.Equal(WindowState.Upcoming, evaluation.WindowAt(Noon));
        Assert.Equal(WindowState.Open, evaluation.WindowAt(Noon.AddMinutes(10)));
        Assert.Equal(WindowState.Open, evaluation.WindowAt(Noon.AddMinutes(70)));
        Assert.Equal(WindowState.Closed, evaluation.WindowAt(Noon.AddMinutes(70).AddSeconds(1)));
    }

    [Fact]
    public void Deadline_IsTheDuration_ButNeverPastTheClosingTime()
    {
        var evaluation = Make(minutes: 30, openForMin: 60);
        Assert.Equal(Noon.AddMinutes(30), evaluation.DeadlineFor(Noon));
        // Started 50 minutes in: the closing time (60) comes before 50 + 30.
        Assert.Equal(Noon.AddMinutes(60), evaluation.DeadlineFor(Noon.AddMinutes(50)));
    }

    [Fact]
    public void Score_IsOutOfTwenty_WithOneDecimal_AndZeroWithNoQuestions()
    {
        Assert.Equal(20m, EvaluationAttempt.ScoreOutOf20(5, 5));
        Assert.Equal(13.3m, EvaluationAttempt.ScoreOutOf20(2, 3));
        Assert.Equal(0m, EvaluationAttempt.ScoreOutOf20(0, 7));
        Assert.Equal(0m, EvaluationAttempt.ScoreOutOf20(0, 0));
        Assert.Equal(67, EvaluationAttempt.Percent(2, 3));
    }

    [Fact]
    public void Attempt_AcceptsAnAnswerOnlyUntilTheDeadlinePlusGrace_AndOnlyOnce()
    {
        var evaluation = Make(minutes: 30);
        var attempt = EvaluationAttempt.Start(Guid.NewGuid(), evaluation, Guid.NewGuid(), Noon);
        Assert.True(attempt.CanSubmitAt(Noon.AddMinutes(30)));
        Assert.True(attempt.CanSubmitAt(Noon.AddMinutes(30).AddSeconds(30)));
        Assert.False(attempt.CanSubmitAt(Noon.AddMinutes(30).AddSeconds(31)));

        attempt.Submit(3, 4, Noon.AddMinutes(30).AddSeconds(10));
        Assert.True(attempt.IsLate);
        Assert.Equal(15m, attempt.Score);
        Assert.False(attempt.CanSubmitAt(Noon.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => attempt.Submit(4, 4, Noon.AddMinutes(31)));
    }

    [Fact]
    public void Question_UsesTheExerciseRules_AndNeedsAKnownCompetency()
    {
        var content = new ExerciseContent(ExerciseKind.Numeric, " 7 ÷ 2 ", null, null, 3.5m, 0, "Porque sí");
        var question = EvaluationQuestion.Create(Guid.NewGuid(), Guid.NewGuid(), 1, new EvaluationQuestionContent(Competencies.Calculation, content));
        Assert.Equal("7 ÷ 2", question.Prompt);
        Assert.True(question.Check("3,5"));
        Assert.False(question.Check("4"));
        Assert.Null(question.Check("tres"));
        Assert.Throws<ArgumentException>(() => EvaluationQuestion.Create(Guid.NewGuid(), Guid.NewGuid(), 1, new EvaluationQuestionContent("otra", content)));
    }

    [Fact]
    public void ByCompetency_CountsEachOne_InCatalogOrder()
    {
        var scores = EvaluationScoring.ByCompetency([
            (Competencies.ProblemSolving, true), (Competencies.Calculation, true), (Competencies.Calculation, false), (Competencies.ProblemSolving, false), (Competencies.ProblemSolving, true)]);
        Assert.Equal([Competencies.Calculation, Competencies.ProblemSolving], scores.Select(score => score.Competency));
        Assert.Equal((1, 2), (scores[0].Correct, scores[0].Total));
        Assert.Equal((2, 3), (scores[1].Correct, scores[1].Total));
        Assert.Equal("Cálculo y operaciones", scores[0].Label);
    }
}

public sealed class StudentEvaluationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid Offering = Guid.NewGuid();

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fakes(params Guid[] offerings) : IStudentCourseAccess, IOfferingDirectory
    {
        public Task<bool> CanStudyAsync(Guid studentUserId, Guid courseId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(studentUserId == Student ? offerings : []);

        public Task<OfferingSummary?> GetAsync(Guid offeringId, CancellationToken cancellationToken) =>
            Task.FromResult<OfferingSummary?>(new OfferingSummary(offeringId, Guid.NewGuid(), "Curso", "Grupo", 8, true));

        public Task<IReadOnlyList<OfferingSummary>> GetManyAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(offeringIds.Select(id => new OfferingSummary(id, Guid.NewGuid(), "Curso", "Grupo", 8, true)).ToArray());

        public Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MemoryRepository : IEvaluationRepository
    {
        public List<Evaluation> Evaluations { get; } = [];
        public List<EvaluationQuestion> Questions { get; } = [];
        public List<EvaluationAttempt> Attempts { get; } = [];
        public List<EvaluationAnswer> Answers { get; } = [];
        public bool NextAddLosesTheRace { get; set; }

        public Task<Evaluation?> GetAsync(Guid evaluationId, CancellationToken cancellationToken) =>
            Task.FromResult(Evaluations.FirstOrDefault(item => item.Id == evaluationId));

        public Task<IReadOnlyList<Evaluation>> ListByOfferingAsync(Guid offeringId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Evaluation>>(Evaluations.Where(item => item.OfferingId == offeringId).ToArray());

        public Task<IReadOnlyList<Evaluation>> ListPublishedAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Evaluation>>(Evaluations.Where(item => item.IsPublished && offeringIds.Contains(item.OfferingId)).ToArray());

        public Task<IReadOnlyList<EvaluationQuestion>> GetQuestionsAsync(Guid evaluationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvaluationQuestion>>(Questions.Where(item => item.EvaluationId == evaluationId).OrderBy(item => item.Position).ToArray());

        public Task<IReadOnlyDictionary<Guid, EvaluationCounts>> CountAsync(IReadOnlyCollection<Guid> evaluationIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, EvaluationCounts>>(evaluationIds.ToDictionary(id => id, id => new EvaluationCounts(
                Questions.Count(q => q.EvaluationId == id), Attempts.Count(a => a.EvaluationId == id), Attempts.Count(a => a.EvaluationId == id && a.IsSubmitted))));

        public Task<IReadOnlyList<EvaluationAttempt>> GetAttemptsOfStudentAsync(Guid studentUserId, IReadOnlyCollection<Guid> evaluationIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvaluationAttempt>>(Attempts.Where(a => a.StudentUserId == studentUserId && evaluationIds.Contains(a.EvaluationId)).ToArray());

        public Task<EvaluationAttempt?> GetAttemptAsync(Guid evaluationId, Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.FirstOrDefault(a => a.EvaluationId == evaluationId && a.StudentUserId == studentUserId));

        public Task<EvaluationAttempt?> GetAttemptByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.FirstOrDefault(a => a.Id == attemptId));

        public Task<IReadOnlyList<EvaluationAttempt>> GetAttemptsAsync(Guid evaluationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvaluationAttempt>>(Attempts.Where(a => a.EvaluationId == evaluationId).ToArray());

        public Task<IReadOnlyList<EvaluationAnswer>> GetAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvaluationAnswer>>(Answers.Where(a => attemptIds.Contains(a.AttemptId)).ToArray());

        public void Add(Evaluation evaluation) => Evaluations.Add(evaluation);

        public void Add(EvaluationQuestion question) => Questions.Add(question);

        public void Add(EvaluationAnswer answer) => Answers.Add(answer);

        public void Remove(Evaluation evaluation) => Evaluations.Remove(evaluation);

        public void Remove(IEnumerable<EvaluationQuestion> questions) => Questions.RemoveAll(questions.ToList().Contains);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TryAddAttemptAsync(EvaluationAttempt attempt, CancellationToken cancellationToken)
        {
            if (NextAddLosesTheRace || Attempts.Any(a => a.EvaluationId == attempt.EvaluationId && a.StudentUserId == attempt.StudentUserId))
            {
                NextAddLosesTheRace = false;
                return Task.FromResult(false);
            }
            Attempts.Add(attempt);
            return Task.FromResult(true);
        }

        public Task<bool> TrySaveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private readonly MemoryRepository repository = new();
    private readonly Clock clock = new(Noon);
    private readonly Evaluation evaluation;

    public StudentEvaluationTests()
    {
        evaluation = Evaluation.Create(Guid.NewGuid(), Offering, Guid.NewGuid(), Guid.NewGuid(), EvaluationKind.Weekly, "Semanal 1",
            Noon.AddMinutes(-10), Noon.AddMinutes(60), 30, true, Noon);
        repository.Add(evaluation);
        repository.Add(Question(1, Competencies.Calculation, new ExerciseContent(ExerciseKind.MultipleChoice, "1/2 + 1/4", ["3/4", "2/6"], 0, null, null, "Común denominador")));
        repository.Add(Question(2, Competencies.ProblemSolving, new ExerciseContent(ExerciseKind.Numeric, "7 ÷ 2", null, null, 3.5m, 0, "7 ÷ 2 = 3,5")));
    }

    private EvaluationQuestion Question(int position, string competency, ExerciseContent content) =>
        EvaluationQuestion.Create(Guid.NewGuid(), evaluation.Id, position, new EvaluationQuestionContent(competency, content));

    private StudentEvaluationUseCases Use(params Guid[] offerings)
    {
        var fakes = new Fakes(offerings.Length == 0 ? [Offering] : offerings);
        return new StudentEvaluationUseCases(repository, fakes, fakes, clock);
    }

    private Dictionary<string, string?> Answers(string first, string second) =>
        new() { [repository.Questions[0].Id.ToString()] = first, [repository.Questions[1].Id.ToString()] = second };

    [Fact]
    public async Task Start_GivesTheQuestionsWithoutTheAnswer_AndTheServerDeadline()
    {
        var result = await Use().StartAsync(Student, evaluation.Id, default);

        Assert.Equal(EvaluationResultStatus.Ok, result.Status);
        var attempt = result.Value!;
        Assert.Equal(2, attempt.Questions.Count);
        Assert.Equal(1800, attempt.DurationSeconds);
        Assert.Equal(Noon.AddMinutes(30), attempt.Deadline);
        Assert.DoesNotContain("Común denominador", System.Text.Json.JsonSerializer.Serialize(attempt));
    }

    [Fact]
    public async Task Start_Twice_ReturnsTheSameAttemptAndDoesNotResetTheClock()
    {
        var first = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;
        clock.Now = Noon.AddMinutes(5);
        var second = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;

        Assert.Equal(first.AttemptId, second.AttemptId);
        Assert.Equal(first.Deadline, second.Deadline);
        Assert.Single(repository.Attempts);
    }

    [Fact]
    public async Task Start_WhenAnotherTabWonTheRace_ReturnsTheAttemptThatWon()
    {
        var winner = EvaluationAttempt.Start(Guid.NewGuid(), evaluation, Student, Noon);
        repository.Attempts.Add(winner);
        repository.NextAddLosesTheRace = true;
        var result = await Use().StartAsync(Student, evaluation.Id, default);
        Assert.Equal(winner.Id, result.Value!.AttemptId);
    }

    [Fact]
    public async Task Start_OutsideTheWindow_OrForAStrangerOrADraft_IsRefused()
    {
        clock.Now = Noon.AddMinutes(-11);
        Assert.Equal(EvaluationResultStatus.Conflict, (await Use().StartAsync(Student, evaluation.Id, default)).Status);
        clock.Now = Noon.AddMinutes(61);
        Assert.Equal(EvaluationResultStatus.Conflict, (await Use().StartAsync(Student, evaluation.Id, default)).Status);
        clock.Now = Noon;
        Assert.Equal(EvaluationResultStatus.NotFound, (await Use().StartAsync(Guid.NewGuid(), evaluation.Id, default)).Status);
        Assert.Equal(EvaluationResultStatus.NotFound, (await Use(Guid.NewGuid()).StartAsync(Student, evaluation.Id, default)).Status);
        evaluation.Update(EvaluationKind.Weekly, "Semanal 1", evaluation.OpensAt, evaluation.ClosesAt, 30, false, Noon);
        Assert.Equal(EvaluationResultStatus.NotFound, (await Use().StartAsync(Student, evaluation.Id, default)).Status);
        Assert.Empty(repository.Attempts);
    }

    [Fact]
    public async Task Submit_CorrectsOnTheServer_AndScoresOutOfTwentyWithCompetencies()
    {
        var attempt = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;
        clock.Now = Noon.AddMinutes(10);
        var result = await Use().SubmitAsync(Student, attempt.AttemptId, Answers("0", "3,5"), default);

        var view = result.Value!;
        Assert.Equal((20m, 100, 2, 2, false), (view.Score, view.Percent, view.CorrectCount, view.Total, view.Late));
        Assert.Equal(2, view.ByCompetency.Count);
        Assert.Equal(2, repository.Answers.Count);

        var again = await Use().SubmitAsync(Student, attempt.AttemptId, Answers("0", "3,5"), default);
        Assert.Equal(EvaluationResultStatus.Conflict, again.Status);
        Assert.Equal(2, repository.Answers.Count);
    }

    [Fact]
    public async Task Submit_WrongUnreadableAndMissingAnswers_CountAsWrong_NeverAsAnError()
    {
        var attempt = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;
        var result = await Use().SubmitAsync(Student, attempt.AttemptId, new Dictionary<string, string?> { [repository.Questions[0].Id.ToString()] = "99" }, default);

        var view = result.Value!;
        Assert.Equal((0m, 0, 2), (view.Score, view.CorrectCount, view.Total));
        Assert.All(view.Questions, question => Assert.False(question.IsCorrect));
        Assert.Equal("Común denominador", view.Questions[0].Explanation);
    }

    [Fact]
    public async Task Submit_AfterTheDeadlinePlusGrace_IsRefused_AndInsideGraceIsMarkedLate()
    {
        var attempt = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;
        clock.Now = Noon.AddMinutes(30).AddSeconds(31);
        Assert.Equal(EvaluationResultStatus.Conflict, (await Use().SubmitAsync(Student, attempt.AttemptId, Answers("0", "3,5"), default)).Status);

        clock.Now = Noon.AddMinutes(30).AddSeconds(20);
        var late = (await Use().SubmitAsync(Student, attempt.AttemptId, Answers("0", "3,5"), default)).Value!;
        Assert.True(late.Late);
    }

    [Fact]
    public async Task Submit_SomeoneElsesAttempt_IsNotFound()
    {
        var attempt = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;
        Assert.Equal(EvaluationResultStatus.NotFound, (await Use().SubmitAsync(Guid.NewGuid(), attempt.AttemptId, Answers("0", "3"), default)).Status);
    }

    [Fact]
    public async Task List_ShowsTheStatusOfEachEvaluation_AndTheScoreOnlyOnceSent()
    {
        var before = (await Use().ListAsync(Student, default)).Single();
        Assert.Equal(("open", null), (before.Status, before.Score));

        var attempt = (await Use().StartAsync(Student, evaluation.Id, default)).Value!;
        Assert.Equal("in_progress", (await Use().ListAsync(Student, default)).Single().Status);

        await Use().SubmitAsync(Student, attempt.AttemptId, Answers("0", "1"), default);
        var after = (await Use().ListAsync(Student, default)).Single();
        Assert.Equal(("graded", 10m, 50), (after.Status, after.Score, after.Percent));

        clock.Now = Noon.AddHours(5);
        Assert.Equal("graded", (await Use().ListAsync(Student, default)).Single().Status);
        Assert.Empty(await Use().ListAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task List_AnEvaluationNobodyStartedBeforeItClosed_IsMissed()
    {
        clock.Now = Noon.AddMinutes(61);
        Assert.Equal("missed", (await Use().ListAsync(Student, default)).Single().Status);
    }
}
