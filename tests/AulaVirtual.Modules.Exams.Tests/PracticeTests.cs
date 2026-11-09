using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Exams.Application.Practice;
using AulaVirtual.Modules.Exams.Domain.Practice;
using AulaVirtual.Modules.Exams.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Exams.Tests;

public sealed class ExerciseTests
{
    private static Exercise MultipleChoice() => Exercise.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        new ExerciseContent(ExerciseKind.MultipleChoice, "¿Cuánto es 1/2 + 1/4?", ["3/4", "2/6", "1/6"], 0, null, null,
            "Se busca el común denominador: 2/4 + 1/4 = 3/4."), DateTimeOffset.UtcNow);

    private static Exercise Numeric(decimal tolerance = 0) => Exercise.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        new ExerciseContent(ExerciseKind.Numeric, "¿Cuánto es 7 ÷ 2?", null, null, 3.5m, tolerance, null), DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", false)]
    [InlineData(" 0 ", true)]
    [InlineData("3", null)]
    [InlineData("-1", null)]
    [InlineData("tres cuartos", null)]
    public void MultipleChoice_IsCheckedByOptionIndex(string answer, bool? expected) =>
        Assert.Equal(expected, MultipleChoice().Check(answer));

    [Theory]
    [InlineData("3.5", true)]
    [InlineData("3,5", true)]
    [InlineData("3,50", true)]
    [InlineData("3", false)]
    [InlineData("1.000,5", null)]
    [InlineData("", null)]
    public void Numeric_AcceptsCommaOrPoint(string answer, bool? expected) =>
        Assert.Equal(expected, Numeric().Check(answer));

    [Fact]
    public void Numeric_HonoursTheTolerance()
    {
        Assert.True(Numeric(0.1m).Check("3.45"));
        Assert.False(Numeric(0.1m).Check("3.7"));
    }

    [Theory]
    [InlineData(new[] { "Solo una" }, 0)]
    [InlineData(new[] { "A", "a" }, 0)]
    [InlineData(new[] { "A", "B" }, 2)]
    public void MultipleChoice_NeedsValidOptionsAndAnswer(string[] options, int correct) =>
        Assert.Throws<ArgumentException>(() => Exercise.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            new ExerciseContent(ExerciseKind.MultipleChoice, "Pregunta", options, correct, null, null, null), DateTimeOffset.UtcNow));
}

public sealed class PracticeUseCaseTests
{
    private readonly Fakes fakes = new();
    private readonly Guid course = Guid.NewGuid();
    private readonly Guid lesson = Guid.NewGuid();
    private readonly Guid student = Guid.NewGuid();

    public PracticeUseCaseTests()
    {
        fakes.Lessons[lesson] = new LessonRef(lesson, course, "Sumar fracciones", true);
        fakes.Enrolled.Add((student, course));
    }

    private PracticeAdminUseCases Admin() => new(fakes, fakes, TimeProvider.System);
    private StudentPracticeUseCases Student() => new(fakes, fakes, fakes, TimeProvider.System);

    private static ExerciseInput Choice(string prompt, Guid? id = null) =>
        new(id, "multiple_choice", prompt, ["Tres cuartos", "Dos sextos"], 0, null, null, "Porque sí.");

    private async Task<IReadOnlyList<ExerciseAdminView>> SeedAsync(params ExerciseInput[] inputs) =>
        (await Admin().ReplaceAsync(course, lesson, inputs, default)).Value!;

    [Fact]
    public async Task TheStudentNeverReceivesTheAnswerBeforeAnswering()
    {
        await SeedAsync(Choice("Pregunta 1"));
        var practice = (await Student().GetLessonAsync(student, lesson, default)).Value!;

        var json = System.Text.Json.JsonSerializer.Serialize(practice);
        Assert.DoesNotContain("CorrectOption", json);
        Assert.DoesNotContain("CorrectNumber", json);
        Assert.DoesNotContain("Explanation", json);
        Assert.DoesNotContain("Porque sí", json);
    }

    [Fact]
    public async Task Answering_IsCorrectedHere_ExplainedAndCountedInProgress()
    {
        var exercises = await SeedAsync(Choice("Pregunta 1"), Choice("Pregunta 2"));

        var wrong = (await Student().AnswerAsync(student, exercises[0].Id, "1", default)).Value!;
        var right = (await Student().AnswerAsync(student, exercises[0].Id, "0", default)).Value!;

        Assert.False(wrong.IsCorrect);
        Assert.Equal("Porque sí.", wrong.Explanation);
        Assert.True(right.IsCorrect);
        Assert.Equal(new Progress(1, 2, 50), right.LessonProgress);
        Assert.Equal(2, right.Attempts);
        Assert.Equal(2, fakes.Attempts.Count);
    }

    [Fact]
    public async Task WithoutAnActiveEnrollment_OrOnADraftLesson_ThereIsNoPractice()
    {
        var exercises = await SeedAsync(Choice("Pregunta 1"));

        Assert.Equal(PracticeResultStatus.NotFound, (await Student().GetLessonAsync(Guid.NewGuid(), lesson, default)).Status);
        Assert.Equal(PracticeResultStatus.NotFound, (await Student().AnswerAsync(Guid.NewGuid(), exercises[0].Id, "0", default)).Status);
        fakes.Lessons[lesson] = fakes.Lessons[lesson] with { IsPublished = false };
        Assert.Equal(PracticeResultStatus.NotFound, (await Student().GetLessonAsync(student, lesson, default)).Status);
        Assert.Empty(fakes.Attempts);
    }

    [Fact]
    public async Task UnreadableAnswers_AreRejectedWithoutBeingRecorded()
    {
        var exercises = await SeedAsync(Choice("Pregunta 1"));
        Assert.Equal(PracticeResultStatus.Invalid, (await Student().AnswerAsync(student, exercises[0].Id, "la primera", default)).Status);
        Assert.Empty(fakes.Attempts);
    }

    [Fact]
    public async Task Attempts_AreCappedPerExercise()
    {
        var exercises = await SeedAsync(Choice("Pregunta 1"));
        for (var i = 0; i < StudentPracticeUseCases.MaxAttemptsPerExercise; i++)
            await Student().AnswerAsync(student, exercises[0].Id, "1", default);

        Assert.Equal(PracticeResultStatus.Conflict, (await Student().AnswerAsync(student, exercises[0].Id, "0", default)).Status);
    }

    [Fact]
    public async Task RemovedExercises_AreArchived_KeepingHistory_AndLeaveTheProgress()
    {
        var exercises = await SeedAsync(Choice("Pregunta 1"), Choice("Pregunta 2"));
        await Student().AnswerAsync(student, exercises[0].Id, "0", default);

        var kept = await SeedAsync(Choice("Pregunta 2 editada", exercises[1].Id));

        Assert.Single(kept);
        Assert.Equal("Pregunta 2 editada", kept[0].Prompt);
        Assert.True(fakes.Exercises.Single(e => e.Id == exercises[0].Id).IsArchived);
        Assert.Single(fakes.Attempts);
        var practice = (await Student().GetLessonAsync(student, lesson, default)).Value!;
        Assert.Equal(new Progress(0, 1, 0), practice.Progress);
    }

    [Fact]
    public async Task Replace_RejectsForeignLessonsExercisesAndBadContent()
    {
        Assert.Equal(PracticeResultStatus.NotFound, (await Admin().ReplaceAsync(Guid.NewGuid(), lesson, [Choice("P")], default)).Status);
        Assert.Equal(PracticeResultStatus.Invalid, (await Admin().ReplaceAsync(course, lesson, [Choice("P", Guid.NewGuid())], default)).Status);
        var bad = await Admin().ReplaceAsync(course, lesson,
            [new ExerciseInput(null, "numeric", "¿7 ÷ 2?", null, null, null, null, null)], default);
        Assert.Equal(PracticeResultStatus.Invalid, bad.Status);
        Assert.Equal("Indica el número correcto.", bad.Detail);
    }

    [Fact]
    public async Task CourseProgress_IsPerPublishedLessonAndOverall()
    {
        var other = Guid.NewGuid();
        fakes.Lessons[other] = new LessonRef(other, course, "Restar fracciones", true);
        var first = await SeedAsync(Choice("A"), Choice("B"));
        await Admin().ReplaceAsync(course, other, [Choice("C"), Choice("D")], default);
        await Student().AnswerAsync(student, first[0].Id, "0", default);
        await Student().AnswerAsync(student, first[1].Id, "0", default);

        var progress = (await Student().GetCourseProgressAsync(student, course, default)).Value!;

        Assert.Equal(new Progress(2, 4, 50), progress.Progress);
        Assert.Equal([100, 0], progress.Lessons.Select(l => l.Progress.Percent!.Value));
    }

    private sealed class Fakes : IPracticeRepository, ILessonDirectory, IStudentCourseAccess
    {
        public Dictionary<Guid, LessonRef> Lessons { get; } = [];
        public HashSet<(Guid Student, Guid Course)> Enrolled { get; } = [];
        public List<Exercise> Exercises { get; } = [];
        public List<ExerciseAttempt> Attempts { get; } = [];

        public Task<LessonRef?> GetLessonAsync(Guid lessonId, CancellationToken cancellationToken) => Task.FromResult(Lessons.GetValueOrDefault(lessonId));
        public Task<IReadOnlyList<LessonRef>> GetPublishedLessonsAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LessonRef>>(Lessons.Values.Where(l => l.CourseId == courseId && l.IsPublished).ToList());
        public Task<bool> CanStudyAsync(Guid studentUserId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Enrolled.Contains((studentUserId, courseId)));

        public Task<IReadOnlyList<Exercise>> GetAllAsync(Guid lessonId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Exercise>>(Exercises.Where(e => e.LessonId == lessonId).OrderBy(e => e.Position).ToList());
        public Task<IReadOnlyList<Exercise>> GetActiveAsync(Guid lessonId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Exercise>>(Exercises.Where(e => e.LessonId == lessonId && !e.IsArchived).OrderBy(e => e.Position).ToList());
        public Task<Exercise?> GetAsync(Guid exerciseId, CancellationToken cancellationToken) => Task.FromResult(Exercises.SingleOrDefault(e => e.Id == exerciseId));
        public Task<IReadOnlyList<ExerciseStatus>> GetStatusAsync(Guid studentUserId, IReadOnlyCollection<Guid> exerciseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExerciseStatus>>(Attempts.Where(a => a.StudentUserId == studentUserId && exerciseIds.Contains(a.ExerciseId))
                .GroupBy(a => a.ExerciseId).Select(g => new ExerciseStatus(g.Key, g.Count(), g.Any(a => a.IsCorrect))).ToList());
        public Task<int> CountAttemptsAsync(Guid studentUserId, Guid exerciseId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.Count(a => a.StudentUserId == studentUserId && a.ExerciseId == exerciseId));
        public Task<IReadOnlyDictionary<Guid, int>> CountActiveAsync(IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(Exercises.Where(e => lessonIds.Contains(e.LessonId) && !e.IsArchived)
                .GroupBy(e => e.LessonId).ToDictionary(g => g.Key, g => g.Count()));
        public Task<IReadOnlyDictionary<(Guid StudentUserId, Guid LessonId), int>> CountSolvedAsync(IReadOnlyCollection<Guid> studentUserIds,
            IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken)
        {
            var active = Exercises.Where(e => !e.IsArchived).Select(e => e.Id).ToHashSet();
            return Task.FromResult<IReadOnlyDictionary<(Guid, Guid), int>>(Attempts
                .Where(a => studentUserIds.Contains(a.StudentUserId) && lessonIds.Contains(a.LessonId) && a.IsCorrect && active.Contains(a.ExerciseId))
                .GroupBy(a => (a.StudentUserId, a.LessonId)).ToDictionary(g => g.Key, g => g.Select(a => a.ExerciseId).Distinct().Count()));
        }
        public Task<(IReadOnlyList<AttemptView> Items, int Total)> GetAttemptsAsync(Guid studentUserId, Guid? lessonId, int page, int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Add(Exercise exercise) => Exercises.Add(exercise);
        public void Add(ExerciseAttempt attempt) => Attempts.Add(attempt);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

// The practice queries translate to PostgreSQL in one statement each.
public sealed class PracticeQueryTests
{
    private readonly SqlCapture capture = new();

    private PracticeRepository Repository() => new(new ExamsDbContext(new DbContextOptionsBuilder<ExamsDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options));

    [Fact]
    public async Task Solved_IsOneGroupedQuery_WithDistinctExercises()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().CountSolvedAsync([Guid.NewGuid()], [Guid.NewGuid()], default));
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("DISTINCT", sql);
        Assert.Contains("is_archived", sql);
    }

    [Fact]
    public async Task StudentStatus_IsOneGroupedQuery()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().GetStatusAsync(Guid.NewGuid(), [Guid.NewGuid()], default));
        Assert.Contains("GROUP BY", sql);
    }

    [Fact]
    public async Task History_IsPagedNewestFirst()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().GetAttemptsAsync(Guid.NewGuid(), null, 1, 20, default));
        Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase);
    }
}
