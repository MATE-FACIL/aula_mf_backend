using AulaVirtual.Modules.Exams.Domain.Diagnostics;

namespace AulaVirtual.Modules.Exams.Application.Diagnostics;

/// <summary>What the team sends for a question; <paramref name="Id"/> keeps an existing one.</summary>
public sealed record DiagnosticQuestionInput(
    Guid? Id,
    string Level,
    string Topic,
    string Kind,
    string Prompt,
    IReadOnlyList<string>? Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal? Tolerance,
    string? Explanation);

/// <summary>The full question, answer included: only for the team.</summary>
public sealed record DiagnosticQuestionAdminView(
    Guid Id,
    int Number,
    string Level,
    string Topic,
    string Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal Tolerance,
    string? Explanation);

/// <summary>A level code and its label, e.g. <c>basic</c> · «Básico».</summary>
public sealed record LevelRef(string Code, string Label);

/// <summary>A line of the result: a topic or a level.</summary>
public sealed record ScoreView(string Label, int Correct, int Total, int Percent);

public sealed record DiagnosticResult(
    IReadOnlyList<ScoreView> Topics,
    IReadOnlyList<ScoreView> Levels,
    LevelRef Suggested,
    LevelRef? Confirmed);

/// <summary>What the student sees of a question: never the answer nor the explanation.</summary>
public sealed record DiagnosticQuestionStudentView(
    Guid Id,
    int Number,
    string Topic,
    string Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    bool Answered);

/// <param name="Status"><c>in_progress</c> or <c>completed</c>.</param>
public sealed record StudentDiagnostic(
    Guid AttemptId,
    string Stage,
    string StageLabel,
    string Status,
    int Answered,
    int Total,
    IReadOnlyList<DiagnosticQuestionStudentView> Questions,
    DiagnosticResult? Result);

/// <summary>One stage in the student's overview: whether there is a test to take and how far they got.</summary>
public sealed record DiagnosticStageSummary(
    string Stage,
    string StageLabel,
    bool Available,
    Guid? AttemptId,
    string? Status,
    int Answered,
    int Total,
    DiagnosticResult? Result);

public sealed record DiagnosticAnswerResult(int Answered, int Total, bool Completed, DiagnosticResult? Result);

public sealed record DiagnosticListItem(
    Guid AttemptId,
    Guid StudentUserId,
    string StudentName,
    string Stage,
    string StageLabel,
    string Status,
    int Answered,
    int Total,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    LevelRef? Suggested,
    LevelRef? Confirmed);

public sealed record DiagnosticPage(IReadOnlyList<DiagnosticListItem> Items, int Page, int PageSize, int Total);

/// <param name="Answer">What the student answered, as text (the option or the number); null if skipped or not yet answered.</param>
public sealed record DiagnosticAnswerReview(
    int Number,
    string Topic,
    string LevelLabel,
    string Prompt,
    bool Answered,
    string? Answer,
    string CorrectAnswer,
    bool IsCorrect,
    string? Explanation);

public sealed record DiagnosticReview(
    DiagnosticListItem Summary,
    DiagnosticResult? Result,
    IReadOnlyList<DiagnosticAnswerReview> Answers,
    string? ConfirmedByName,
    DateTimeOffset? ConfirmedAt);

/// <summary><c>pending</c>: finished, waiting for the teacher. <c>confirmed</c>, <c>in_progress</c> or <c>all</c>.</summary>
public enum DiagnosticFilter
{
    All,
    Pending,
    Confirmed,
    InProgress
}

public interface IDiagnosticRepository
{
    /// <summary>Tracked, archived included, in order (the team's replace operation).</summary>
    Task<IReadOnlyList<DiagnosticQuestion>> GetAllQuestionsAsync(string stage, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiagnosticQuestion>> GetActiveQuestionsAsync(string stage, CancellationToken cancellationToken);

    /// <summary>Some questions by id, archived included (an attempt keeps its questions).</summary>
    Task<IReadOnlyList<DiagnosticQuestion>> GetQuestionsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken);

    /// <summary>Active questions per stage, in one grouped query.</summary>
    Task<IReadOnlyDictionary<string, int>> CountActiveQuestionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DiagnosticAttempt>> GetAttemptsOfAsync(Guid studentUserId, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<DiagnosticAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>The answers of some attempts, in one query.</summary>
    Task<IReadOnlyList<DiagnosticAnswer>> GetAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken);

    /// <summary>Answers per attempt, in one grouped query.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken);

    /// <summary>Newest first, paged in the database.</summary>
    Task<(IReadOnlyList<DiagnosticAttempt> Items, int Total)> SearchAttemptsAsync(DiagnosticFilter filter, string? stage, int page,
        int pageSize, CancellationToken cancellationToken);

    void Add(DiagnosticQuestion question);

    void Add(DiagnosticAttempt attempt);

    void Add(DiagnosticAnswer answer);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>False when a unique rule stopped the save (a second start or a repeated answer at the same time).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

public enum DiagnosticResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

public sealed record DiagnosticOutcome<T>(DiagnosticResultStatus Status, T? Value, string? Detail)
{
    public static DiagnosticOutcome<T> Ok(T value) => new(DiagnosticResultStatus.Ok, value, null);
    public static DiagnosticOutcome<T> NotFound(string detail) => new(DiagnosticResultStatus.NotFound, default, detail);
    public static DiagnosticOutcome<T> Invalid(string detail) => new(DiagnosticResultStatus.Invalid, default, detail);
    public static DiagnosticOutcome<T> Conflict(string detail) => new(DiagnosticResultStatus.Conflict, default, detail);
}
