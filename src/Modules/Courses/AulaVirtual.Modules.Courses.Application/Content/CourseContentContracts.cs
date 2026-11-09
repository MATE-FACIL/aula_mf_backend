using AulaVirtual.Modules.Courses.Domain.Content;

namespace AulaVirtual.Modules.Courses.Application.Content;

public sealed record MaterialView(Guid Id, string Title, string Url);

/// <summary><paramref name="Number"/> is the place of the lesson in the whole course: 1, 2, 3… across units.</summary>
public sealed record LessonView(
    Guid Id,
    Guid UnitId,
    int Number,
    string Title,
    string? Summary,
    string? VideoUrl,
    bool IsPublished,
    IReadOnlyList<MaterialView> Materials);

public sealed record UnitView(Guid Id, string Title, int Position, IReadOnlyList<LessonView> Lessons);

public sealed record CourseContentView(Guid CourseId, string CourseTitle, IReadOnlyList<UnitView> Units);

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
    Conflict
}

public sealed record ContentResult<T>(ContentResultStatus Status, T? Value, string? Detail)
{
    public static ContentResult<T> Ok(T value) => new(ContentResultStatus.Ok, value, null);
    public static ContentResult<T> NotFound(string detail) => new(ContentResultStatus.NotFound, default, detail);
    public static ContentResult<T> Invalid(string detail) => new(ContentResultStatus.Invalid, default, detail);
    public static ContentResult<T> Conflict(string detail) => new(ContentResultStatus.Conflict, default, detail);
}
