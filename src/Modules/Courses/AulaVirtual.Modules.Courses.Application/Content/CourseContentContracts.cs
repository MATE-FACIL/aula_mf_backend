using AulaVirtual.Modules.Courses.Domain.Content;

namespace AulaVirtual.Modules.Courses.Application.Content;

/// <summary>
/// A material. <paramref name="Url"/> is the link, or for an uploaded file (<paramref name="FileId"/>) a signed download
/// URL that expires; null when file storage is unavailable.
/// </summary>
public sealed record MaterialView(Guid Id, string Title, string? Url, Guid? FileId = null, string? ContentType = null, long? Size = null);

/// <summary>
/// <paramref name="Number"/> is the place of the lesson in the whole course: 1, 2, 3… across units.
/// <paramref name="VideoUrl"/> is the external link or, for an uploaded video (<paramref name="VideoFileId"/>), a signed URL.
/// </summary>
public sealed record LessonView(
    Guid Id,
    Guid UnitId,
    int Number,
    string Title,
    string? Summary,
    string? VideoUrl,
    bool IsPublished,
    IReadOnlyList<MaterialView> Materials,
    Guid? VideoFileId = null,
    string? VideoContentType = null);

public sealed record UnitView(Guid Id, string Title, int Position, IReadOnlyList<LessonView> Lessons);

public sealed record CourseContentView(Guid CourseId, string CourseTitle, IReadOnlyList<UnitView> Units);

/// <summary>What the administrator sends: files are referenced by the id returned when the upload was granted.</summary>
public sealed record LessonInput(
    string Title,
    string? Summary,
    string? VideoUrl,
    Guid? VideoFileId,
    bool IsPublished,
    IReadOnlyList<MaterialInput> Materials);

public sealed record MaterialInput(string Title, string? Url, Guid? FileId);

/// <summary>Where and how to upload a file straight to storage, before <paramref name="ExpiresAt"/>.</summary>
public sealed record UploadGrant(
    Guid FileId,
    Uri Url,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt,
    long MaxBytes);

// Cross-module read contract: what students see of a course (only published lessons, units without any
// published lesson left out). Enrollments decides whether the student may see the course at all.
public interface ICourseContentDirectory
{
    /// <summary>Null when the course does not exist. Fixed cost: four queries whatever the size of the course.</summary>
    Task<CourseContentView?> GetPublishedContentAsync(Guid courseId, CancellationToken cancellationToken);
}

public interface ICourseContentRepository
{
    Task<bool> CourseExistsAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>The whole course as the administrator sees it, drafts included. Null when the course does not exist.</summary>
    Task<CourseContentView?> GetContentAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>Only published lessons. Null when the course does not exist.</summary>
    Task<CourseContentView?> GetPublishedContentAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>Tracked, in order.</summary>
    Task<IReadOnlyList<CourseUnit>> GetUnitsAsync(Guid courseId, CancellationToken cancellationToken);

    Task<CourseUnit?> GetUnitAsync(Guid courseId, Guid unitId, CancellationToken cancellationToken);

    /// <summary>Tracked, with its materials.</summary>
    Task<Lesson?> GetLessonAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken);

    /// <summary>Tracked, in order.</summary>
    Task<IReadOnlyList<Lesson>> GetUnitLessonsAsync(Guid unitId, CancellationToken cancellationToken);

    Task<int> CountLessonsAsync(Guid courseId, CancellationToken cancellationToken);

    Task<bool> UnitHasLessonsAsync(Guid unitId, CancellationToken cancellationToken);

    /// <summary>Position for a lesson appended at the end of the unit.</summary>
    Task<int> NextLessonPositionAsync(Guid unitId, CancellationToken cancellationToken);

    void Add(CourseUnit unit);

    void Add(Lesson lesson);

    void Remove(CourseUnit unit);

    void Remove(Lesson lesson);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public enum ContentResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
    // File storage is not configured in this environment.
    Unavailable
}

public sealed record ContentResult<T>(ContentResultStatus Status, T? Value, string? Detail)
{
    public static ContentResult<T> Ok(T value) => new(ContentResultStatus.Ok, value, null);
    public static ContentResult<T> NotFound(string detail) => new(ContentResultStatus.NotFound, default, detail);
    public static ContentResult<T> Invalid(string detail) => new(ContentResultStatus.Invalid, default, detail);
    public static ContentResult<T> Conflict(string detail) => new(ContentResultStatus.Conflict, default, detail);
    public static ContentResult<T> Unavailable(string detail) => new(ContentResultStatus.Unavailable, default, detail);
}
