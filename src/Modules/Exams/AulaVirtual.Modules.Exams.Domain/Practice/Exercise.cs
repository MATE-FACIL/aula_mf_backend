using System.Globalization;

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
    public const int MaxPromptLength = 1000;
    public const int MaxExplanationLength = 2000;
    public const int MaxOptionLength = 200;
    public const int MinOptions = 2;
    public const int MaxOptions = 6;
    public const int MaxAnswerLength = 50;
    public const decimal MaxTolerance = 1_000_000m;

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
    public bool? Check(string? answer)
    {
        var text = answer?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxAnswerLength) return null;
        if (Kind == ExerciseKind.MultipleChoice)
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var option) && option < Options.Length
                ? option == CorrectOption
                : null;
        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number & ~NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var number)
            ? Math.Abs(number - CorrectNumber!.Value) <= Tolerance
            : null;
    }

    private void Apply(ExerciseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(content.Kind)) throw new ArgumentException("Tipo de ejercicio desconocido.", nameof(content));
        var prompt = Text(content.Prompt, MaxPromptLength, "El enunciado", required: true)!;
        var explanation = Text(content.Explanation, MaxExplanationLength, "La explicación", required: false);

        string[] options = [];
        int? correctOption = null;
        decimal? correctNumber = null;
        decimal tolerance = 0;
        if (content.Kind == ExerciseKind.MultipleChoice)
        {
            options = (content.Options ?? []).Select(option => Text(option, MaxOptionLength, "Cada opción", required: true)!).ToArray();
            if (options.Length is < MinOptions or > MaxOptions)
                throw new ArgumentException($"Un ejercicio de opción múltiple tiene entre {MinOptions} y {MaxOptions} opciones.", nameof(content));
            if (options.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != options.Length)
                throw new ArgumentException("Las opciones no se pueden repetir.", nameof(content));
            if (content.CorrectOption is not { } correct || correct < 0 || correct >= options.Length)
                throw new ArgumentException("Indica cuál de las opciones es la correcta.", nameof(content));
            correctOption = correct;
        }
        else
        {
            correctNumber = content.CorrectNumber ?? throw new ArgumentException("Indica el número correcto.", nameof(content));
            tolerance = content.Tolerance ?? 0;
            if (tolerance is < 0 or > MaxTolerance)
                throw new ArgumentException("La tolerancia debe ser cero o un número positivo.", nameof(content));
        }

        Kind = content.Kind;
        Prompt = prompt;
        Explanation = explanation;
        Options = options;
        CorrectOption = correctOption;
        CorrectNumber = correctNumber;
        Tolerance = tolerance;
    }

    private static string? Text(string? value, int maxLength, string label, bool required)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return required ? throw new ArgumentException($"{label} es obligatorio.", nameof(value)) : null;
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{label} admite como máximo {maxLength} caracteres.", nameof(value));
        return trimmed;
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
