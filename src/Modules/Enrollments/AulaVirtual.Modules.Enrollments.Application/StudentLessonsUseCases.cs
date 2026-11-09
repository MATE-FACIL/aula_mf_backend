using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Application.Offerings;

namespace AulaVirtual.Modules.Enrollments.Application;

/// <summary><paramref name="Level"/>: the Mathematics level of the course (SCRUM-39), or null for any other course.</summary>
public sealed record StudentCourse(Guid CourseId, string CourseTitle, Guid OfferingId, string OfferingName, LevelOfCourse? Level = null);

// What a student may study (SCRUM-55): the courses of their ACTIVE enrollments in published groups, and the
// published lessons of each. Another course, a pending enrollment or a draft lesson are simply not there:
// a lesson list never says whether a course exists for someone who cannot see it.
public sealed class StudentLessonsUseCases(
    IEnrollmentRepository enrollments,
    IOfferingDirectory offerings,
    ICourseContentDirectory content,
    IMathLevelDirectory levels)
{
    /// <summary>Three queries: active enrollments, their editions, and the level of those courses.</summary>
    public async Task<IReadOnlyList<StudentCourse>> ListCoursesAsync(Guid studentUserId,
        CancellationToken cancellationToken)
    {
        var offeringIds = await enrollments.GetActiveOfferingIdsAsync(studentUserId, cancellationToken);
        if (offeringIds.Count == 0) return [];

        var published = (await offerings.GetManyAsync(offeringIds, cancellationToken))
            .Where(offering => offering.IsPublished)
            .ToArray();
        if (published.Length == 0) return [];

        // Same DbContext (Courses) as the editions: one after the other.
        var levelOf = (await levels.GetLevelsAsync(published.Select(offering => offering.CourseId).ToHashSet(), cancellationToken))
            .ToDictionary(level => level.CourseId);
        return published
            .OrderBy(offering => offering.CourseTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(offering => new StudentCourse(offering.CourseId, offering.CourseTitle, offering.Id, offering.Name,
                levelOf.GetValueOrDefault(offering.CourseId)))
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
