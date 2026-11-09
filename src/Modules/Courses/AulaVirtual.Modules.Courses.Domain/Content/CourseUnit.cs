namespace AulaVirtual.Modules.Courses.Domain.Content;

// A block of lessons inside a course (SCRUM-55), e.g. «Unidad 1 · Fracciones». Units are ordered by Position.
public sealed class CourseUnit
{
    public const int MaxTitleLength = 150;

    private CourseUnit()
    {
    }

    private CourseUnit(Guid id, Guid courseId, string title, int position, DateTimeOffset createdAt)
    {
        Id = id;
        CourseId = courseId;
        Title = title;
        Position = position;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public int Position { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static CourseUnit Create(Guid id, Guid courseId, string title, int position, DateTimeOffset createdAt)
    {
        ContentRules.RequireId(id, nameof(id));
        ContentRules.RequireId(courseId, nameof(courseId));
        return new CourseUnit(id, courseId, ContentRules.Title(title, MaxTitleLength, nameof(title)),
            ContentRules.Position(position), createdAt);
    }

    public void Rename(string title, DateTimeOffset now)
    {
        Title = ContentRules.Title(title, MaxTitleLength, nameof(title));
        UpdatedAt = now;
    }

    public void MoveTo(int position, DateTimeOffset now)
    {
        Position = ContentRules.Position(position);
        UpdatedAt = now;
    }
}

internal static class ContentRules
{
    public const int MaxUrlLength = 2048;

    public static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", name);
    }

    public static string Title(string? value, int maxLength, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("El título es obligatorio.", name);
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"El título admite como máximo {maxLength} caracteres.", name);
        return trimmed;
    }

    public static string? OptionalText(string? value, int maxLength, string label, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{label} admite como máximo {maxLength} caracteres.", name);
        return trimmed;
    }

    /// <summary>Only HTTPS: the classroom is served over HTTPS and an HTTP video or file would be blocked.</summary>
    public static string? OptionalHttpsUrl(string? value, string label, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : HttpsUrl(value, label, name);

    public static string HttpsUrl(string? value, string label, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxUrlLength ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"{label} debe ser un enlace https:// válido.", name);
        return trimmed;
    }

    public static StoredFile File(StoredFile file, string name)
    {
        RequireId(file.FileId, name);
        if (string.IsNullOrWhiteSpace(file.ContentType) || file.Size <= 0)
            throw new ArgumentException("El archivo subido no es válido.", name);
        return file with { ContentType = file.ContentType.Trim() };
    }

    public static int Position(int position) =>
        position < 1 ? throw new ArgumentOutOfRangeException(nameof(position), "Positions start at 1.") : position;
}
