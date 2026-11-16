using AulaVirtual.Modules.Exams.Domain.Evaluations;

namespace AulaVirtual.Modules.Exams.Application.Evaluations;

// ─── What the teacher sends ────────────────────────────────────────────

/// <summary>One question: the competency it measures plus an ordinary exercise (multiple choice or numeric).</summary>
public sealed record EvaluationQuestionInput(
    string? Competency,
    string? Kind,
    string? Prompt,
    IReadOnlyList<string>? Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal? Tolerance,
    string? Explanation);

/// <summary>An evaluation as a whole: its data and its ordered questions (replaced together).</summary>
public sealed record EvaluationInput(
    string? Kind,
    string? Title,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    int? DurationMinutes,
    bool IsPublished,
    IReadOnlyList<EvaluationQuestionInput>? Questions);

// ─── What the teacher sees ─────────────────────────────────────────────

public sealed record EvaluationSummaryView(
    Guid Id,
    Guid OfferingId,
    Guid CourseId,
    string Kind,
    string Title,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    int DurationMinutes,
    bool IsPublished,
    int QuestionCount,
    int Started,
    int Submitted,
    int Enrolled);

/// <summary>A question with its answer: only for the teacher of the group.</summary>
public sealed record EvaluationQuestionAdminView(
    Guid Id,
    int Number,
    string Competency,
    string CompetencyLabel,
    string Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal Tolerance,
    string? Explanation);

public sealed record EvaluationDetailView(EvaluationSummaryView Evaluation, IReadOnlyList<EvaluationQuestionAdminView> Questions);

public sealed record CompetencyScore(string Competency, string Label, int Correct, int Total);

/// <summary><paramref name="Status"/>: graded, in_progress (started, not sent) or not_started.</summary>
public sealed record StudentEvaluationResultView(
    Guid StudentUserId,
    string FullName,
    string Status,
    decimal? Score,
    int? Percent,
    DateTimeOffset? SubmittedAt,
    bool IsLate,
    IReadOnlyList<CompetencyScore> ByCompetency);

public sealed record EvaluationResultsView(
    EvaluationSummaryView Evaluation,
    decimal? AverageScore,
    IReadOnlyList<CompetencyScore> ByCompetency,
    IReadOnlyList<StudentEvaluationResultView> Students);

// ─── What the student sees ─────────────────────────────────────────────

/// <summary>
/// <paramref name="Status"/>: upcoming, open, in_progress, graded, or missed (the window closed without sending it).
/// <paramref name="Score"/> is out of 20.
/// </summary>
public sealed record StudentEvaluationView(
    Guid Id,
    string Kind,
    string Title,
    string CourseTitle,
    string OfferingName,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    int DurationMinutes,
    int QuestionCount,
    string Status,
    decimal? Score,
    int? Percent,
    Guid? AttemptId);

/// <summary>A question as the student gets it: never the correct answer nor the explanation.</summary>
public sealed record EvaluationQuestionStudentView(Guid Id, int Number, string Kind, string Competency, string Prompt, IReadOnlyList<string> Options);

public sealed record StartedAttemptView(
    Guid AttemptId,
    Guid EvaluationId,
    string Title,
    int DurationSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    DateTimeOffset ServerNow,
    IReadOnlyList<EvaluationQuestionStudentView> Questions);

public sealed record QuestionFeedback(Guid QuestionId, bool IsCorrect, string? Explanation);

public sealed record SubmittedResultView(
    Guid EvaluationId,
    decimal Score,
    int Percent,
    int CorrectCount,
    int Total,
    bool Late,
    IReadOnlyList<CompetencyScore> ByCompetency,
    IReadOnlyList<QuestionFeedback> Questions);

// ─── Results and persistence ───────────────────────────────────────────

public enum EvaluationResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

public sealed record EvaluationOutcome<T>(EvaluationResultStatus Status, T? Value, string? Detail)
{
    public static EvaluationOutcome<T> Ok(T value) => new(EvaluationResultStatus.Ok, value, null);
    public static EvaluationOutcome<T> NotFound(string detail) => new(EvaluationResultStatus.NotFound, default, detail);
    public static EvaluationOutcome<T> Invalid(string detail) => new(EvaluationResultStatus.Invalid, default, detail);
    public static EvaluationOutcome<T> Conflict(string detail) => new(EvaluationResultStatus.Conflict, default, detail);
}

public sealed record EvaluationCounts(int Questions, int Started, int Submitted);

public interface IEvaluationRepository
{
    Task<Evaluation?> GetAsync(Guid evaluationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Evaluation>> ListByOfferingAsync(Guid offeringId, CancellationToken cancellationToken);

    /// <summary>Published evaluations of the groups, closing soonest first.</summary>
    Task<IReadOnlyList<Evaluation>> ListPublishedAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken);

    /// <summary>Tracked, in order (replace operation).</summary>
    Task<IReadOnlyList<EvaluationQuestion>> GetQuestionsAsync(Guid evaluationId, CancellationToken cancellationToken);

    /// <summary>Questions, started and sent attempts per evaluation, in grouped queries.</summary>
    Task<IReadOnlyDictionary<Guid, EvaluationCounts>> CountAsync(IReadOnlyCollection<Guid> evaluationIds, CancellationToken cancellationToken);

    /// <summary>The student's attempt in each evaluation, in one query.</summary>
    Task<IReadOnlyList<EvaluationAttempt>> GetAttemptsOfStudentAsync(Guid studentUserId, IReadOnlyCollection<Guid> evaluationIds,
        CancellationToken cancellationToken);

    Task<EvaluationAttempt?> GetAttemptAsync(Guid evaluationId, Guid studentUserId, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<EvaluationAttempt?> GetAttemptByIdAsync(Guid attemptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EvaluationAttempt>> GetAttemptsAsync(Guid evaluationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EvaluationAnswer>> GetAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken);

    void Add(Evaluation evaluation);

    void Add(EvaluationQuestion question);

    void Add(EvaluationAnswer answer);

    void Remove(Evaluation evaluation);

    void Remove(IEnumerable<EvaluationQuestion> questions);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Adds the attempt; false (and nothing saved) when the student already has one: two tabs started it at once.</summary>
    Task<bool> TryAddAttemptAsync(EvaluationAttempt attempt, CancellationToken cancellationToken);

    /// <summary>Saves the pending changes; false when a unique rule rejected them (the same attempt sent twice at once).</summary>
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);
}
