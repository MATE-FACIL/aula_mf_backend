using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Offerings;

namespace AulaVirtual.Modules.Enrollments.Application;

public sealed record StudentCourse(Guid CourseId, string CourseTitle, Guid OfferingId, string OfferingName);

// What a student may study (SCRUM-55): the courses of their ACTIVE enrollments in published groups, and the
// published lessons of each. Another course, a pending enrollment or a draft lesson are simply not there:
// a lesson list never says whether a course exists for someone who cannot see it.
public sealed class StudentLessonsUseCases(
    IEnrollmentRepository enrollments,
    IOfferingDirectory offerings,
    ICourseContentDirectory content)
{
    /// <summary>Two queries: active enrollments, then their editions.</summary>
    public async Task<IReadOnlyList<StudentCourse>> ListCoursesAsync(Guid studentUserId,
        CancellationToken cancellationToken)
    {
        var offeringIds = await enrollments.GetActiveOfferingIdsAsync(studentUserId, cancellationToken);
        if (offeringIds.Count == 0) return [];

        return (await offerings.GetManyAsync(offeringIds, cancellationToken))
            .Where(offering => offering.IsPublished)
            .OrderBy(offering => offering.CourseTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(offering => new StudentCourse(offering.CourseId, offering.CourseTitle, offering.Id, offering.Name))
            .ToArray();
    }

    /// <summary>Null when the student is not actively enrolled in the course.</summary>
    public async Task<CourseContentView?> GetLessonsAsync(Guid studentUserId, Guid courseId,
        CancellationToken cancellationToken)
    {
        var courses = await ListCoursesAsync(studentUserId, cancellationToken);
        if (!courses.Any(course => course.CourseId == courseId)) return null;
        return await content.GetPublishedContentAsync(courseId, cancellationToken);
    }
}
