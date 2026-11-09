using AulaVirtual.Modules.Exams.Domain.Practice;

namespace AulaVirtual.Modules.Exams.Application.Practice;

/// <summary>What the team sends; <paramref name="Id"/> keeps an existing exercise (and its students' history).</summary>
public sealed record ExerciseInput(
    Guid? Id,
    string Kind,
    string Prompt,
    IReadOnlyList<string>? Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal? Tolerance,
    string? Explanation);

/// <summary>The full exercise, answer included: only for the team.</summary>
public sealed record ExerciseAdminView(
    Guid Id,
    int Number,
    string Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal Tolerance,
    string? Explanation);

/// <summary>
/// What a student sees before answering: never the correct answer nor the explanation. <paramref name="Solved"/> is
/// true once they answered it right at least once.
/// </summary>
public sealed record ExerciseStudentView(
    Guid Id,
    int Number,
    string Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    int Attempts,
    bool Solved);

/// <summary><paramref name="Percent"/>: exercises solved over exercises available; null when there are none.</summary>
public sealed record Progress(int Solved, int Total, int? Percent)
{
    public static Progress Of(int solved, int total) =>
        new(solved, total, total == 0 ? null : (int)Math.Round(solved * 100.0 / total, MidpointRounding.AwayFromZero));
}

public sealed record LessonPractice(Guid LessonId, string LessonTitle, Progress Progress, IReadOnlyList<ExerciseStudentView> Exercises);

/// <summary>The result of one answer: right or wrong, the explanation, and the lesson progress after it.</summary>
public sealed record AnswerResult(bool IsCorrect, string? Explanation, Progress LessonProgress, int Attempts);

public sealed record LessonProgress(Guid LessonId, string Title, Progress Progress);

/// <summary>Progress in a course (a Mathematics level is a course): per published lesson and overall.</summary>
public sealed record CourseProgress(Guid CourseId, Progress Progress, IReadOnlyList<LessonProgress> Lessons);

public sealed record AttemptView(Guid ExerciseId, Guid LessonId, string Prompt, string Answer, bool IsCorrect, DateTimeOffset AnsweredAt);

public sealed record AttemptPage(IReadOnlyList<AttemptView> Items, int Page, int PageSize, int Total);

public sealed record ExerciseStatus(Guid ExerciseId, int Attempts, bool Solved);

public interface IPracticeRepository
{
    /// <summary>Tracked, archived included, in order (the team's view and the replace operation).</summary>
    Task<IReadOnlyList<Exercise>> GetAllAsync(Guid lessonId, CancellationToken cancellationToken);

    /// <summary>Active exercises of a lesson, in order.</summary>
    Task<IReadOnlyList<Exercise>> GetActiveAsync(Guid lessonId, CancellationToken cancellationToken);

    Task<Exercise?> GetAsync(Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>Attempts and «solved» for one student over some exercises, in one grouped query.</summary>
    Task<IReadOnlyList<ExerciseStatus>> GetStatusAsync(Guid studentUserId, IReadOnlyCollection<Guid> exerciseIds,
        CancellationToken cancellationToken);

    Task<int> CountAttemptsAsync(Guid studentUserId, Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>Active exercises per lesson, in one grouped query.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountActiveAsync(IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken);

    /// <summary>Distinct active exercises solved, per (student, lesson), in one grouped query.</summary>
    Task<IReadOnlyDictionary<(Guid StudentUserId, Guid LessonId), int>> CountSolvedAsync(
        IReadOnlyCollection<Guid> studentUserIds, IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken);

    /// <summary>A student's answers, newest first, paged in the database.</summary>
    Task<(IReadOnlyList<AttemptView> Items, int Total)> GetAttemptsAsync(Guid studentUserId, Guid? lessonId, int page, int pageSize,
        CancellationToken cancellationToken);

    void Add(Exercise exercise);

    void Add(ExerciseAttempt attempt);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

// Cross-module read contract (teacher panel, SCRUM-69): how far each student of a group got in the course practice.
public interface IPracticeProgressDirectory
{
    Task<IReadOnlyDictionary<Guid, Progress>> GetCourseProgressAsync(IReadOnlyCollection<Guid> studentUserIds, Guid courseId,
        CancellationToken cancellationToken);
}

public enum PracticeResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

public sealed record PracticeResult<T>(PracticeResultStatus Status, T? Value, string? Detail)
{
    public static PracticeResult<T> Ok(T value) => new(PracticeResultStatus.Ok, value, null);
    public static PracticeResult<T> NotFound(string detail) => new(PracticeResultStatus.NotFound, default, detail);
    public static PracticeResult<T> Invalid(string detail) => new(PracticeResultStatus.Invalid, default, detail);
    public static PracticeResult<T> Conflict(string detail) => new(PracticeResultStatus.Conflict, default, detail);
}
