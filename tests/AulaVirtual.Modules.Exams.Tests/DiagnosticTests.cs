using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Exams.Application.Diagnostics;
using AulaVirtual.Modules.Exams.Domain.Diagnostics;
using AulaVirtual.Modules.Exams.Domain.Practice;
using AulaVirtual.Modules.Exams.Infrastructure.Persistence;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Exams.Tests;

public sealed class DiagnosticScoringTests
{
    private static readonly DiagnosticAttempt Attempt = DiagnosticAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "primary",
        Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray(), DateTimeOffset.UtcNow);

    /// <param name="lines">(level, topic, correct) per answer, in order.</param>
    private static IReadOnlyList<DiagnosticAnswer> Answers(params (string Level, string Topic, bool Correct)[] lines) =>
        lines.Select((line, index) => DiagnosticAnswer.Record(Guid.NewGuid(), Attempt,
            DiagnosticQuestion.Create(Attempt.QuestionIds[index], "primary", line.Level, line.Topic, index + 1,
                new ExerciseContent(ExerciseKind.Numeric, "¿2 + 2?", null, null, 4, null, null), DateTimeOffset.UtcNow),
            line.Correct ? "4" : null, line.Correct, DateTimeOffset.UtcNow)).ToArray();

    [Fact]
    public void SuggestsTheFirstLevelNotMasteredYet()
    {
        var score = DiagnosticScoring.Score(Answers(
            ("prebasic", "Conteo", true), ("prebasic", "Conteo", true),
            ("basic", "Fracciones", true), ("basic", "Fracciones", true),
            ("intermediate", "Decimales", true), ("intermediate", "Decimales", false)));

        // Básico mastered (100 %), Intermedio not yet (50 % < 60 %).
        Assert.Equal("intermediate", score.SuggestedLevel);
        Assert.Equal([100, 100, 50], score.Levels.Select(line => line.Percent));
    }

    [Fact]
    public void SixtyPercentMastersALevel()
    {
        var score = DiagnosticScoring.Score(Answers(
            ("prebasic", "Conteo", true), ("prebasic", "Conteo", true), ("prebasic", "Conteo", true),
            ("prebasic", "Conteo", false), ("prebasic", "Conteo", false),
            ("basic", "Fracciones", false)));

        Assert.Equal("basic", score.SuggestedLevel);
    }

    [Fact]
    public void MasteringEveryLevelSuggestsTheHighestMeasured()
    {
        var score = DiagnosticScoring.Score(Answers(("prebasic", "Conteo", true), ("basic", "Fracciones", true)));
        Assert.Equal("basic", score.SuggestedLevel);
    }

    [Fact]
    public void FailingTheFirstLevelSuggestsIt()
    {
        var score = DiagnosticScoring.Score(Answers(("prebasic", "Conteo", false), ("basic", "Fracciones", true)));
        Assert.Equal("prebasic", score.SuggestedLevel);
    }

    [Fact]
    public void TopicsKeepTheirOrderAndMergeIgnoringCase()
    {
        var score = DiagnosticScoring.Score(Answers(
            ("prebasic", "Fracciones", true), ("prebasic", "Geometría", false), ("basic", "fracciones", false)));

        Assert.Equal(["Fracciones", "Geometría"], score.Topics.Select(line => line.Key));
        Assert.Equal((1, 2), (score.Topics[0].Correct, score.Topics[0].Total));
    }
}

public sealed class DiagnosticDomainTests
{
    [Fact]
    public void AQuestionNeedsAKnownLevelAndATopic()
    {
        var content = new ExerciseContent(ExerciseKind.Numeric, "¿2 + 2?", null, null, 4, null, null);
        Assert.Throws<ArgumentException>(() => DiagnosticQuestion.Create(Guid.NewGuid(), "primary", "expert", "Suma", 1, content, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => DiagnosticQuestion.Create(Guid.NewGuid(), "primary", "basic", " ", 1, content, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => DiagnosticQuestion.Create(Guid.NewGuid(), "college", "basic", "Suma", 1, content, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void OnlyACompletedDiagnosticCanBeConfirmed()
    {
        var attempt = DiagnosticAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "secondary", [Guid.NewGuid()], DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => attempt.Confirm("basic", Guid.NewGuid(), DateTimeOffset.UtcNow));

        attempt.Complete("basic", DateTimeOffset.UtcNow);
        attempt.Confirm("intermediate", Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(("basic", "intermediate"), (attempt.SuggestedLevel, attempt.ConfirmedLevel));
    }

    [Fact]
    public void ASkippedQuestionIsNeverCorrect()
    {
        var question = DiagnosticQuestion.Create(Guid.NewGuid(), "primary", "basic", "Suma", 1,
            new ExerciseContent(ExerciseKind.Numeric, "¿2 + 2?", null, null, 4, null, null), DateTimeOffset.UtcNow);
        var attempt = DiagnosticAttempt.Start(Guid.NewGuid(), Guid.NewGuid(), "primary", [question.Id], DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => DiagnosticAnswer.Record(Guid.NewGuid(), attempt, question, null, true, DateTimeOffset.UtcNow));
    }
}

public sealed class DiagnosticUseCaseTests
{
    private readonly Fakes fakes = new();
    private readonly Guid student = Guid.NewGuid();
    private readonly Guid teacher = Guid.NewGuid();

    public DiagnosticUseCaseTests()
    {
        fakes.Roles[student] = [Roles.Student];
        fakes.Names[student] = "Ana Pérez";
        fakes.Names[teacher] = "Luis Rojas";
    }

    private DiagnosticBankUseCases Bank() => new(fakes, TimeProvider.System);
    private StudentDiagnosticUseCases Student() => new(fakes, fakes, TimeProvider.System);
    private DiagnosticReviewUseCases Review() => new(fakes, fakes, TimeProvider.System);

    private static DiagnosticQuestionInput Choice(string level, string topic, Guid? id = null) =>
        new(id, level, topic, "multiple_choice", $"Pregunta de {topic}", ["Bien", "Mal"], 0, null, null, "La primera es la correcta.");

    private async Task<IReadOnlyList<DiagnosticQuestionAdminView>> SeedAsync() =>
        (await Bank().ReplaceAsync("primary", [
            Choice("prebasic", "Conteo"), Choice("prebasic", "Conteo"),
            Choice("basic", "Fracciones"), Choice("basic", "Fracciones")
        ], default)).Value!;

    [Fact]
    public async Task TheStudentNeverReceivesTheAnswers()
    {
        await SeedAsync();
        var diagnostic = (await Student().StartAsync(student, "primary", default)).Value!;

        Assert.Equal(4, diagnostic.Total);
        Assert.Equal("in_progress", diagnostic.Status);
        Assert.Null(diagnostic.Result);
        var json = System.Text.Json.JsonSerializer.Serialize(diagnostic);
        Assert.DoesNotContain("correct", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartingAgainReturnsTheSameDiagnostic()
    {
        await SeedAsync();
        var first = (await Student().StartAsync(student, "primary", default)).Value!;
        var second = (await Student().StartAsync(student, "primary", default)).Value!;
        Assert.Equal(first.AttemptId, second.AttemptId);
        Assert.Single(fakes.Attempts);
    }

    [Fact]
    public async Task OnlyStudentsCanStart_AndOnlyWithQuestions()
    {
        Assert.Equal(DiagnosticResultStatus.Conflict, (await Student().StartAsync(student, "primary", default)).Status);
        await SeedAsync();
        Assert.Equal(DiagnosticResultStatus.NotFound, (await Student().StartAsync(teacher, "primary", default)).Status);
        Assert.Equal(DiagnosticResultStatus.Invalid, (await Student().StartAsync(student, "college", default)).Status);
    }

    [Fact]
    public async Task TheLastAnswerFinishesAndSuggestsALevel()
    {
        await SeedAsync();
        var diagnostic = (await Student().StartAsync(student, "primary", default)).Value!;
        var ids = diagnostic.Questions.Select(q => q.Id).ToArray();

        await Student().AnswerAsync(student, diagnostic.AttemptId, ids[0], "0", false, default);
        await Student().AnswerAsync(student, diagnostic.AttemptId, ids[1], "0", false, default);
        var third = (await Student().AnswerAsync(student, diagnostic.AttemptId, ids[2], "1", false, default)).Value!;
        Assert.False(third.Completed);
        var last = (await Student().AnswerAsync(student, diagnostic.AttemptId, ids[3], null, true, default)).Value!;

        Assert.True(last.Completed);
        Assert.Equal("basic", last.Result!.Suggested.Code);
        Assert.Equal("Básico", last.Result.Suggested.Label);
        Assert.Equal(["Conteo", "Fracciones"], last.Result.Topics.Select(t => t.Label));
        Assert.Equal([100, 0], last.Result.Topics.Select(t => t.Percent));
        Assert.Equal(DiagnosticResultStatus.Conflict,
            (await Student().AnswerAsync(student, diagnostic.AttemptId, ids[0], "0", false, default)).Status);
    }

    [Fact]
    public async Task Answers_RejectUnreadableAnswersAndOtherStudents()
    {
        await SeedAsync();
        var diagnostic = (await Student().StartAsync(student, "primary", default)).Value!;
        var id = diagnostic.Questions[0].Id;

        Assert.Equal(DiagnosticResultStatus.Invalid, (await Student().AnswerAsync(student, diagnostic.AttemptId, id, "7", false, default)).Status);
        Assert.Equal(DiagnosticResultStatus.NotFound, (await Student().AnswerAsync(teacher, diagnostic.AttemptId, id, "0", false, default)).Status);
        Assert.Equal(DiagnosticResultStatus.NotFound,
            (await Student().AnswerAsync(student, diagnostic.AttemptId, Guid.NewGuid(), "0", false, default)).Status);
        Assert.Empty(fakes.Answers);
    }

    [Fact]
    public async Task EditingTheBankDoesNotChangeAStartedDiagnostic()
    {
        var seeded = await SeedAsync();
        var diagnostic = (await Student().StartAsync(student, "primary", default)).Value!;
        await Bank().ReplaceAsync("primary", [Choice("basic", "Fracciones", seeded[2].Id)], default);

        var again = (await Student().GetAsync(student, diagnostic.AttemptId, default)).Value!;
        Assert.Equal(4, again.Questions.Count);
        Assert.Equal(3, fakes.Questions.Count(q => q.IsArchived));
    }

    [Fact]
    public async Task TheTeacherSeesEachAnswerAndConfirmsTheLevel()
    {
        await SeedAsync();
        var diagnostic = (await Student().StartAsync(student, "primary", default)).Value!;
        foreach (var question in diagnostic.Questions)
            await Student().AnswerAsync(student, diagnostic.AttemptId, question.Id, "1", false, default);

        var pending = await Review().ListAsync(DiagnosticFilter.Pending, null, 1, 20, default);
        var item = Assert.Single(pending.Items);
        Assert.Equal(("Ana Pérez", "prebasic", 4), (item.StudentName, item.Suggested!.Code, item.Answered));

        var detail = (await Review().GetAsync(diagnostic.AttemptId, default)).Value!;
        Assert.Equal(("Mal", "Bien", false), (detail.Answers[0].Answer, detail.Answers[0].CorrectAnswer, detail.Answers[0].IsCorrect));

        var confirmed = (await Review().ConfirmAsync(diagnostic.AttemptId, "basic", teacher, default)).Value!;
        Assert.Equal(("basic", "Luis Rojas"), (confirmed.Summary.Confirmed!.Code, confirmed.ConfirmedByName));
        Assert.Empty((await Review().ListAsync(DiagnosticFilter.Pending, null, 1, 20, default)).Items);

        var overview = await Student().OverviewAsync(student, default);
        Assert.Equal("basic", overview.Single(s => s.Stage == "primary").Result!.Confirmed!.Code);
    }

    [Fact]
    public async Task ConfirmingNeedsAFinishedDiagnosticAndAKnownLevel()
    {
        await SeedAsync();
        var diagnostic = (await Student().StartAsync(student, "primary", default)).Value!;
        Assert.Equal(DiagnosticResultStatus.Invalid, (await Review().ConfirmAsync(diagnostic.AttemptId, "expert", teacher, default)).Status);
        Assert.Equal(DiagnosticResultStatus.Conflict, (await Review().ConfirmAsync(diagnostic.AttemptId, "basic", teacher, default)).Status);
        Assert.Equal(DiagnosticResultStatus.NotFound, (await Review().ConfirmAsync(Guid.NewGuid(), "basic", teacher, default)).Status);
    }

    [Fact]
    public async Task Bank_ReportsWhichQuestionIsWrong()
    {
        var bad = await Bank().ReplaceAsync("primary", [Choice("basic", "Suma"), Choice("expert", "Resta")], default);
        Assert.Equal(DiagnosticResultStatus.Invalid, bad.Status);
        Assert.StartsWith("Pregunta 2:", bad.Detail);
    }

    private sealed class Fakes : IDiagnosticRepository, IRoleDirectory, IUserDirectory
    {
        public Dictionary<Guid, IReadOnlyList<string>> Roles { get; } = [];
        public Dictionary<Guid, string> Names { get; } = [];
        public List<DiagnosticQuestion> Questions { get; } = [];
        public List<DiagnosticAttempt> Attempts { get; } = [];
        public List<DiagnosticAnswer> Answers { get; } = [];

        public Task<IReadOnlyList<DiagnosticQuestion>> GetAllQuestionsAsync(string stage, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiagnosticQuestion>>(Questions.Where(q => q.Stage == stage).OrderBy(q => q.Position).ToList());
        public Task<IReadOnlyList<DiagnosticQuestion>> GetActiveQuestionsAsync(string stage, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiagnosticQuestion>>(Questions.Where(q => q.Stage == stage && !q.IsArchived).OrderBy(q => q.Position).ToList());
        public Task<IReadOnlyList<DiagnosticQuestion>> GetQuestionsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiagnosticQuestion>>(Questions.Where(q => questionIds.Contains(q.Id)).ToList());
        public Task<IReadOnlyDictionary<string, int>> CountActiveQuestionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(Questions.Where(q => !q.IsArchived).GroupBy(q => q.Stage).ToDictionary(g => g.Key, g => g.Count()));
        public Task<IReadOnlyList<DiagnosticAttempt>> GetAttemptsOfAsync(Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiagnosticAttempt>>(Attempts.Where(a => a.StudentUserId == studentUserId).ToList());
        public Task<DiagnosticAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.SingleOrDefault(a => a.Id == attemptId));
        public Task<IReadOnlyList<DiagnosticAnswer>> GetAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiagnosticAnswer>>(Answers.Where(a => attemptIds.Contains(a.AttemptId)).ToList());
        public Task<IReadOnlyDictionary<Guid, int>> CountAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(Answers.Where(a => attemptIds.Contains(a.AttemptId)).GroupBy(a => a.AttemptId).ToDictionary(g => g.Key, g => g.Count()));
        public Task<(IReadOnlyList<DiagnosticAttempt> Items, int Total)> SearchAttemptsAsync(DiagnosticFilter filter, string? stage, int page,
            int pageSize, CancellationToken cancellationToken)
        {
            var items = Attempts.Where(a => filter switch
            {
                DiagnosticFilter.Pending => a.Status == DiagnosticStatus.Completed && a.ConfirmedLevel is null,
                DiagnosticFilter.Confirmed => a.ConfirmedLevel is not null,
                DiagnosticFilter.InProgress => a.Status == DiagnosticStatus.InProgress,
                _ => true
            } && (stage is null || a.Stage == stage)).ToList();
            return Task.FromResult<(IReadOnlyList<DiagnosticAttempt>, int)>((items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count));
        }
        public void Add(DiagnosticQuestion question) => Questions.Add(question);
        public void Add(DiagnosticAttempt attempt) => Attempts.Add(attempt);
        public void Add(DiagnosticAnswer answer)
        {
            // Same rule as the unique index: one answer per question and attempt.
            if (Answers.Any(a => a.AttemptId == answer.AttemptId && a.QuestionId == answer.QuestionId)) duplicate = true;
            else Answers.Add(answer);
        }
        private bool duplicate;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
        {
            var ok = !duplicate;
            duplicate = false;
            return Task.FromResult(ok);
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(Roles.Where(r => userIds.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value));
        public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSummary>>(Names.Where(n => userIds.Contains(n.Key))
                .Select(n => new UserSummary(n.Key, n.Value.Split(' ')[0], n.Value.Split(' ')[1], null, null, null, null, DateTimeOffset.UtcNow, 0)).ToList());
        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

// The diagnostic queries translate to PostgreSQL in one statement each.
public sealed class DiagnosticQueryTests
{
    private readonly SqlCapture capture = new();

    private DiagnosticRepository Repository() => new(new ExamsDbContext(new DbContextOptionsBuilder<ExamsDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options));

    [Fact]
    public async Task QuestionsPerStage_IsOneGroupedQuery()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().CountActiveQuestionsAsync(default));
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("is_archived", sql);
    }

    [Fact]
    public async Task AnswersPerAttempt_IsOneGroupedQuery()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().CountAnswersAsync([Guid.NewGuid(), Guid.NewGuid()], default));
        Assert.Contains("GROUP BY", sql);
    }

    [Fact]
    public async Task PendingList_IsPagedAndFiltered()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().SearchAttemptsAsync(DiagnosticFilter.Pending, "primary", 1, 20, default));
        Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmed_level IS NULL", sql);
    }
}
