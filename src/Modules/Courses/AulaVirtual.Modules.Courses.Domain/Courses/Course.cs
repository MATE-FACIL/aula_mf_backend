namespace AulaVirtual.Modules.Courses.Domain.Courses;

public sealed class Course
{
    private Course()
    {
    }

    private Course(Guid id, string title, Guid? categoryId, DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        CategoryId = categoryId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public Guid? CategoryId { get; private set; }
    public bool IsPublished { get; private set; }
    public string? ShortDescription { get; private set; }
    public string? FullDescription { get; private set; }
    public string? Objectives { get; private set; }
    public string? Syllabus { get; private set; }
    public string? TargetAudience { get; private set; }
    public string? Prerequisites { get; private set; }
    // Default length of each class (SCRUM-42: primary 60 min, secondary 90 min). Null = not configured.
    public int? SessionDurationMinutes { get; private set; }
    public string? CoverObjectKey { get; private set; }
    public string? CoverContentType { get; private set; }
    public long? CoverSize { get; private set; }
    public string? CoverOriginalFileName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Course Create(Guid id, string title, Guid? categoryId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A course requires an identifier.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(title));
        }

        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("A category identifier cannot be empty.", nameof(categoryId));
        }

        return new Course(id, trimmedTitle, categoryId, createdAt);
    }

    public void AssignCategory(Guid categoryId, DateTimeOffset updatedAt)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("A category identifier cannot be empty.", nameof(categoryId));
        }

        CategoryId = categoryId;
        UpdatedAt = updatedAt;
    }

    public void Update(string title, Guid? categoryId, string? shortDescription,
        string? fullDescription, string? objectives, string? syllabus,
        string? targetAudience, string? prerequisites, DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        title = title.Trim();
        if (title.Length > 200) throw new ArgumentOutOfRangeException(nameof(title));
        if (categoryId == Guid.Empty) throw new ArgumentException("A category identifier cannot be empty.", nameof(categoryId));
        if (IsPublished && categoryId is null)
            throw new InvalidOperationException("A published course requires a category.");
        Title = title;
        CategoryId = categoryId;
        ShortDescription = Normalize(shortDescription);
        FullDescription = Normalize(fullDescription);
        Objectives = Normalize(objectives);
        Syllabus = Normalize(syllabus);
        TargetAudience = Normalize(targetAudience);
        Prerequisites = Normalize(prerequisites);
        UpdatedAt = updatedAt;
    }

    public void ReplaceCover(string objectKey, string contentType, long size,
        string? originalFileName, DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        CoverObjectKey = objectKey;
        CoverContentType = contentType;
        CoverSize = size;
        CoverOriginalFileName = originalFileName;
        UpdatedAt = updatedAt;
    }

    public void RestoreCoverAfterFailedSave(string? objectKey, string? contentType,
        long? size, string? originalFileName, DateTimeOffset? updatedAt)
    {
        CoverObjectKey = objectKey;
        CoverContentType = contentType;
        CoverSize = size;
        CoverOriginalFileName = originalFileName;
        UpdatedAt = updatedAt;
    }

    public const int MinSessionMinutes = 30;
    public const int MaxSessionMinutes = 240;

    public void SetSessionDuration(int? minutes, DateTimeOffset updatedAt)
    {
        if (minutes is < MinSessionMinutes or > MaxSessionMinutes)
            throw new ArgumentOutOfRangeException(nameof(minutes),
                $"Session duration must be between {MinSessionMinutes} and {MaxSessionMinutes} minutes.");
        if (SessionDurationMinutes == minutes) return;
        SessionDurationMinutes = minutes;
        UpdatedAt = updatedAt;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Publish(DateTimeOffset updatedAt)
    {
        if (CategoryId is null)
        {
            throw new InvalidOperationException("A course requires a category before publication.");
        }

        IsPublished = true;
        UpdatedAt = updatedAt;
    }

    public void Unpublish(DateTimeOffset updatedAt)
    {
        IsPublished = false;
        UpdatedAt = updatedAt;
    }
}
