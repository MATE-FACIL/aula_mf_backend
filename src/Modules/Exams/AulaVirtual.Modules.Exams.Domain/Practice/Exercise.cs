namespace AulaVirtual.Modules.Exams.Domain.Practice;

public enum ExerciseKind
{
    MultipleChoice,
    Numeric
}

public static class ExerciseKinds
{
    public const string MultipleChoice = "multiple_choice";
    public const string Numeric = "numeric";

    public static string ToCode(ExerciseKind kind) => kind == ExerciseKind.MultipleChoice ? MultipleChoice : Numeric;

    public static bool TryParse(string? code, out ExerciseKind kind)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case MultipleChoice: kind = ExerciseKind.MultipleChoice; return true;
            case Numeric: kind = ExerciseKind.Numeric; return true;
            default: kind = default; return false;
        }
    }
}

/// <summary>
/// What the team writes for an exercise. Multiple choice uses <paramref name="Options"/> and
/// <paramref name="CorrectOption"/> (0-based); numeric uses <paramref name="CorrectNumber"/> and an optional tolerance.
/// </summary>
public sealed record ExerciseContent(
    ExerciseKind Kind,
    string Prompt,
    IReadOnlyList<string>? Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal? Tolerance,
    string? Explanation);

// A practice exercise of a lesson (SCRUM-59), corrected on the server: the correct answer never leaves it until the
// student has answered, and then only through the explanation. An exercise removed from a lesson is archived, not
// deleted, so students keep their attempt history (SCRUM-60).
public sealed class Exercise
{
    public const int MaxPromptLength = ExerciseRules.MaxPromptLength;
    public const int MaxExplanationLength = ExerciseRules.MaxExplanationLength;
    public const int MaxOptionLength = ExerciseRules.MaxOptionLength;
    public const int MinOptions = ExerciseRules.MinOptions;
    public const int MaxOptions = ExerciseRules.MaxOptions;
    public const int MaxAnswerLength = ExerciseRules.MaxAnswerLength;
    public const decimal MaxTolerance = ExerciseRules.MaxTolerance;

    private Exercise()
    {
    }

    private Exercise(Guid id, Guid lessonId, Guid courseId, int position, DateTimeOffset createdAt)
    {
        Id = id;
        LessonId = lessonId;
        CourseId = courseId;
        Position = position;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid CourseId { get; private set; }
    public int Position { get; private set; }
    public ExerciseKind Kind { get; private set; }
    public string Prompt { get; private set; } = string.Empty;
    public string[] Options { get; private set; } = [];
    public int? CorrectOption { get; private set; }
    public decimal? CorrectNumber { get; private set; }
    public decimal Tolerance { get; private set; }
    public string? Explanation { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Exercise Create(Guid id, Guid lessonId, Guid courseId, int position, ExerciseContent content,
        DateTimeOffset createdAt)
    {
        RequireId(id, nameof(id));
        RequireId(lessonId, nameof(lessonId));
        RequireId(courseId, nameof(courseId));
        var exercise = new Exercise(id, lessonId, courseId, RequirePosition(position), createdAt);
        exercise.Apply(content);
        return exercise;
    }

    public void Update(ExerciseContent content, int position, DateTimeOffset now)
    {
        Apply(content);
        Position = RequirePosition(position);
        IsArchived = false;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (IsArchived) return;
        IsArchived = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Corrects an answer: the option index for multiple choice, a number (comma or point as decimal separator)
    /// for numeric exercises. Null when the answer cannot be read at all.
    /// </summary>
    public bool? Check(string? answer) => ExerciseRules.Check(Kind, Options, CorrectOption, CorrectNumber, Tolerance, answer);

    private void Apply(ExerciseContent content)
    {
        var rules = ExerciseRules.Normalize(content);
        Kind = rules.Kind;
        Prompt = rules.Prompt;
        Explanation = rules.Explanation;
        Options = rules.Options;
        CorrectOption = rules.CorrectOption;
        CorrectNumber = rules.CorrectNumber;
        Tolerance = rules.Tolerance;
    }

    private static int RequirePosition(int position) =>
        position < 1 ? throw new ArgumentOutOfRangeException(nameof(position), "Positions start at 1.") : position;

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", name);
    }
}

/// <summary>One answer of a student to an exercise. Every answer is kept: it is the student's history (SCRUM-60).</summary>
public sealed class ExerciseAttempt
{
    private ExerciseAttempt()
    {
    }

    private ExerciseAttempt(Guid id, Guid exerciseId, Guid lessonId, Guid courseId, Guid studentUserId, string answer,
        bool isCorrect, DateTimeOffset answeredAt)
    {
        Id = id;
        ExerciseId = exerciseId;
        LessonId = lessonId;
        CourseId = courseId;
        StudentUserId = studentUserId;
        Answer = answer;
        IsCorrect = isCorrect;
        AnsweredAt = answeredAt;
    }

    public Guid Id { get; private set; }
    public Guid ExerciseId { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public string Answer { get; private set; } = string.Empty;
    public bool IsCorrect { get; private set; }
    public DateTimeOffset AnsweredAt { get; private set; }

    public static ExerciseAttempt Record(Guid id, Exercise exercise, Guid studentUserId, string answer, bool isCorrect,
        DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        if (id == Guid.Empty || studentUserId == Guid.Empty) throw new ArgumentException("Identifiers are required.");
        return new ExerciseAttempt(id, exercise.Id, exercise.LessonId, exercise.CourseId, studentUserId, answer.Trim(),
            isCorrect, answeredAt);
    }
}
