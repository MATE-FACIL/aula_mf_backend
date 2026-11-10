using System.Globalization;

namespace AulaVirtual.Modules.Exams.Domain.Practice;

/// <summary>An exercise's content once validated and trimmed.</summary>
public sealed record NormalizedExercise(
    ExerciseKind Kind,
    string Prompt,
    string? Explanation,
    string[] Options,
    int? CorrectOption,
    decimal? CorrectNumber,
    decimal Tolerance);

// The rules every corrected question follows, whether it is a lesson exercise (SCRUM-59) or a diagnostic question
// (SCRUM-48): how its content is validated and how an answer is corrected. One place, so both stay identical.
public static class ExerciseRules
{
    public const int MaxPromptLength = 1000;
    public const int MaxExplanationLength = 2000;
    public const int MaxOptionLength = 200;
    public const int MinOptions = 2;
    public const int MaxOptions = 6;
    public const int MaxAnswerLength = 50;
    public const decimal MaxTolerance = 1_000_000m;

    public static NormalizedExercise Normalize(ExerciseContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(content.Kind)) throw new ArgumentException("Tipo de ejercicio desconocido.", nameof(content));
        var prompt = Text(content.Prompt, MaxPromptLength, "El enunciado", required: true)!;
        var explanation = Text(content.Explanation, MaxExplanationLength, "La explicación", required: false);

        if (content.Kind == ExerciseKind.MultipleChoice)
        {
            var options = (content.Options ?? []).Select(option => Text(option, MaxOptionLength, "Cada opción", required: true)!).ToArray();
            if (options.Length is < MinOptions or > MaxOptions)
                throw new ArgumentException($"Un ejercicio de opción múltiple tiene entre {MinOptions} y {MaxOptions} opciones.", nameof(content));
            if (options.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != options.Length)
                throw new ArgumentException("Las opciones no se pueden repetir.", nameof(content));
            if (content.CorrectOption is not { } correct || correct < 0 || correct >= options.Length)
                throw new ArgumentException("Indica cuál de las opciones es la correcta.", nameof(content));
            return new NormalizedExercise(content.Kind, prompt, explanation, options, correct, null, 0);
        }

        var correctNumber = content.CorrectNumber ?? throw new ArgumentException("Indica el número correcto.", nameof(content));
        var tolerance = content.Tolerance ?? 0;
        if (tolerance is < 0 or > MaxTolerance)
            throw new ArgumentException("La tolerancia debe ser cero o un número positivo.", nameof(content));
        return new NormalizedExercise(content.Kind, prompt, explanation, [], null, correctNumber, tolerance);
    }

    /// <summary>
    /// Corrects an answer: the option index for multiple choice, a number (comma or point as decimal separator)
    /// for numeric questions. Null when the answer cannot be read at all.
    /// </summary>
    public static bool? Check(ExerciseKind kind, IReadOnlyList<string> options, int? correctOption, decimal? correctNumber,
        decimal tolerance, string? answer)
    {
        var text = answer?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxAnswerLength) return null;
        if (kind == ExerciseKind.MultipleChoice)
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var option) && option < options.Count
                ? option == correctOption
                : null;
        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number & ~NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var number)
            ? Math.Abs(number - correctNumber!.Value) <= tolerance
            : null;
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
}
