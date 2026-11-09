namespace AulaVirtual.Modules.Courses.Application.Content;

public sealed record LessonRef(Guid LessonId, Guid CourseId, string Title, bool IsPublished);

// Cross-module read contract (practice, SCRUM-59/60): which course a lesson belongs to, whether students can see
// it, and the published lessons of a course in order. Cheap, indexed lookups without loading the course content.
public interface ILessonDirectory
{
    Task<LessonRef?> GetLessonAsync(Guid lessonId, CancellationToken cancellationToken);

    /// <summary>Published lessons of the course, in course order (unit position, then lesson position).</summary>
    Task<IReadOnlyList<LessonRef>> GetPublishedLessonsAsync(Guid courseId, CancellationToken cancellationToken);
}
