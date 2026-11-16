using AulaVirtual.Modules.Exams.Domain.Practice;

namespace AulaVirtual.Modules.Exams.Domain.Evaluations;

public enum EvaluationKind
{
    Weekly,
    Midterm,
    Final
}

public static class EvaluationKinds
{
    public const string Weekly = "weekly";
    public const string Midterm = "midterm";
    public const string Final = "final";

    public static string ToCode(EvaluationKind kind) => kind switch
    {
        EvaluationKind.Midterm => Midterm,
        EvaluationKind.Final => Final,
        _ => Weekly
    };

    public static bool TryParse(string? code, out EvaluationKind kind)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case Weekly: kind = EvaluationKind.Weekly; return true;
            case Midterm: kind = EvaluationKind.Midterm; return true;
            case Final: kind = EvaluationKind.Final; return true;
            default: kind = default; return false;
        }
    }
}

/// <summary>
/// The competencies a question can measure (SCRUM-62). Generic for now: the academic team can replace them without
/// touching the rest, because questions only store the code.
/// </summary>
public static class Competencies
{
    public const string Calculation = "calculo";
    public const string Reasoning = "razonamiento";
    public const string ProblemSolving = "problemas";

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Calculation] = "Cálculo y operaciones",
        [Reasoning] = "Razonamiento lógico",
        [ProblemSolving] = "Resolución de problemas"
    };

    public static IReadOnlyDictionary<string, string> All => Labels;

    public static bool IsValid(string? code) => code is not null && Labels.ContainsKey(code);

    /// <summary>The label of a competency; the code itself if it is unknown (old data must still read).</summary>
    public static string LabelOf(string code) => Labels.TryGetValue(code, out var label) ? label : code;
}

/// <summary>What the teacher writes for one question: an exercise plus the competency it measures.</summary>
public sealed record EvaluationQuestionContent(string Competency, ExerciseContent Exercise);

/// <summary>
/// The question of an evaluation, corrected on the server (SCRUM-62). Same rules as a practice exercise
/// (<see cref="ExerciseRules"/>); the correct answer reaches the student only through the explanation after submitting.
/// </summary>
public sealed class EvaluationQuestion
{
    private EvaluationQuestion()
    {
    }

    public Guid Id { get; private set; }
    public Guid EvaluationId { get; private set; }
    public int Position { get; private set; }
    public string Competency { get; private set; } = string.Empty;
    public ExerciseKind Kind { get; private set; }
    public string Prompt { get; private set; } = string.Empty;
    public string[] Options { get; private set; } = [];
    public int? CorrectOption { get; private set; }
    public decimal? CorrectNumber { get; private set; }
    public decimal Tolerance { get; private set; }
    public string? Explanation { get; private set; }

    public static EvaluationQuestion Create(Guid id, Guid evaluationId, int position, EvaluationQuestionContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (id == Guid.Empty || evaluationId == Guid.Empty) throw new ArgumentException("Identifiers are required.");
        if (position < 1) throw new ArgumentOutOfRangeException(nameof(position), "Positions start at 1.");
        if (!Competencies.IsValid(content.Competency)) throw new ArgumentException("La competencia no es válida.", nameof(content));
        var rules = ExerciseRules.Normalize(content.Exercise);
        return new EvaluationQuestion
        {
            Id = id,
            EvaluationId = evaluationId,
            Position = position,
            Competency = content.Competency,
            Kind = rules.Kind,
            Prompt = rules.Prompt,
            Explanation = rules.Explanation,
            Options = rules.Options,
            CorrectOption = rules.CorrectOption,
            CorrectNumber = rules.CorrectNumber,
            Tolerance = rules.Tolerance
        };
    }

    /// <summary>True/false, or null when the answer cannot be read at all (it counts as wrong, never as an error).</summary>
    public bool? Check(string? answer) => ExerciseRules.Check(Kind, Options, CorrectOption, CorrectNumber, Tolerance, answer);
}

/// <summary>
/// An evaluation of a group (SCRUM-62): weekly, midterm (session 8) or final (session 14). The server owns the time:
/// it can be started only between <see cref="OpensAt"/> and <see cref="ClosesAt"/>, and each attempt has
/// <see cref="DurationMinutes"/> to finish. A draft is invisible to students.
/// </summary>
public sealed class Evaluation
{
    public const int MaxTitleLength = 150;
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 180;
    public const int MaxQuestions = 50;

    private Evaluation()
    {
    }

    public Guid Id { get; private set; }
    public Guid OfferingId { get; private set; }
    public Guid CourseId { get; private set; }
    public EvaluationKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateTimeOffset OpensAt { get; private set; }
    public DateTimeOffset ClosesAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public bool IsPublished { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Evaluation Create(Guid id, Guid offeringId, Guid courseId, Guid createdByUserId, EvaluationKind kind, string? title,
        DateTimeOffset opensAt, DateTimeOffset closesAt, int durationMinutes, bool isPublished, DateTimeOffset now)
    {
        if (id == Guid.Empty || offeringId == Guid.Empty || courseId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Identifiers are required.");
        var evaluation = new Evaluation
        {
            Id = id, OfferingId = offeringId, CourseId = courseId, CreatedByUserId = createdByUserId, CreatedAt = now
        };
        evaluation.Apply(kind, title, opensAt, closesAt, durationMinutes, isPublished);
        return evaluation;
    }

    public void Update(EvaluationKind kind, string? title, DateTimeOffset opensAt, DateTimeOffset closesAt, int durationMinutes,
        bool isPublished, DateTimeOffset now)
    {
        Apply(kind, title, opensAt, closesAt, durationMinutes, isPublished);
        UpdatedAt = now;
    }

    /// <summary>The state of the evaluation at <paramref name="now"/>, from the point of view of the window alone.</summary>
    public WindowState WindowAt(DateTimeOffset now) =>
        now < OpensAt ? WindowState.Upcoming : now > ClosesAt ? WindowState.Closed : WindowState.Open;

    /// <summary>When an attempt started at <paramref name="startedAt"/> must be over: the duration, never past the closing time.</summary>
    public DateTimeOffset DeadlineFor(DateTimeOffset startedAt)
    {
        var byDuration = startedAt.AddMinutes(DurationMinutes);
        return byDuration < ClosesAt ? byDuration : ClosesAt;
    }

    private void Apply(EvaluationKind kind, string? title, DateTimeOffset opensAt, DateTimeOffset closesAt, int durationMinutes,
        bool isPublished)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Tipo de evaluación desconocido.", nameof(kind));
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("El título es obligatorio.", nameof(title));
        if (trimmed.Length > MaxTitleLength) throw new ArgumentException($"El título admite como máximo {MaxTitleLength} caracteres.", nameof(title));
        if (closesAt <= opensAt) throw new ArgumentException("El cierre debe ser posterior a la apertura.", nameof(closesAt));
        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
            throw new ArgumentException($"El tiempo para rendirla es de {MinDurationMinutes} a {MaxDurationMinutes} minutos.", nameof(durationMinutes));
        Kind = kind;
        Title = trimmed;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        DurationMinutes = durationMinutes;
        IsPublished = isPublished;
    }
}

public enum WindowState
{
    Upcoming,
    Open,
    Closed
}

/// <summary>
/// A student's try at an evaluation: one per student (unique), started inside the window and submitted before its
/// deadline. <see cref="Score"/> is out of 20; <see cref="IsLate"/> when it arrived inside the grace period.
/// </summary>
public sealed class EvaluationAttempt
{
    /// <summary>Network and clock slack: an answer sent a few seconds after the deadline still counts, flagged as late.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private EvaluationAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid EvaluationId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public int CorrectCount { get; private set; }
    public int QuestionCount { get; private set; }
    public decimal? Score { get; private set; }
    public bool IsLate { get; private set; }

    public bool IsSubmitted => SubmittedAt is not null;

    public static EvaluationAttempt Start(Guid id, Evaluation evaluation, Guid studentUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (id == Guid.Empty || studentUserId == Guid.Empty) throw new ArgumentException("Identifiers are required.");
        // PostgreSQL keeps microseconds: truncating here makes what we return now identical to what a reload returns.
        var started = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        return new EvaluationAttempt
        {
            Id = id, EvaluationId = evaluation.Id, StudentUserId = studentUserId, StartedAt = started, Deadline = evaluation.DeadlineFor(started)
        };
    }

    public bool CanSubmitAt(DateTimeOffset now) => !IsSubmitted && now <= Deadline + Grace;

    public void Submit(int correct, int total, DateTimeOffset now)
    {
        if (IsSubmitted) throw new InvalidOperationException("La evaluación ya se envió.");
        CorrectCount = correct;
        QuestionCount = total;
        Score = ScoreOutOf20(correct, total);
        SubmittedAt = now;
        IsLate = now > Deadline;
    }

    /// <summary>Correct over total, out of 20, one decimal; 0 with no questions.</summary>
    public static decimal ScoreOutOf20(int correct, int total) =>
        total <= 0 ? 0 : Math.Round(correct * 20m / total, 1, MidpointRounding.AwayFromZero);

    public static int Percent(int correct, int total) =>
        total <= 0 ? 0 : (int)Math.Round(correct * 100.0 / total, MidpointRounding.AwayFromZero);
}

/// <summary>One answer of an attempt, kept with its competency so a result by competency never needs the questions.</summary>
public sealed class EvaluationAnswer
{
    private EvaluationAnswer()
    {
    }

    public Guid Id { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid QuestionId { get; private set; }
    public string Competency { get; private set; } = string.Empty;
    public string? Answer { get; private set; }
    public bool IsCorrect { get; private set; }

    public static EvaluationAnswer Record(Guid id, Guid attemptId, EvaluationQuestion question, string? answer, bool isCorrect)
    {
        ArgumentNullException.ThrowIfNull(question);
        var text = answer?.Trim();
        if (text is { Length: > ExerciseRules.MaxAnswerLength }) text = text[..ExerciseRules.MaxAnswerLength];
        return new EvaluationAnswer
        {
            Id = id, AttemptId = attemptId, QuestionId = question.Id, Competency = question.Competency,
            Answer = string.IsNullOrEmpty(text) ? null : text, IsCorrect = isCorrect
        };
    }
}
