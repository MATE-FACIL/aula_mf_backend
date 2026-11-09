using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Domain.Offerings;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

// Read side for the teacher panel and practice. Filter and order on the entities, project last.
public sealed class TeachingDirectory(CoursesDbContext db, TimeProvider timeProvider) : ITeachingDirectory, ILessonDirectory
{
    public async Task<IReadOnlyList<TeacherGroup>> GetGroupsAsync(Guid teacherUserId, CancellationToken cancellationToken)
    {
        // 1) The teacher's groups with their course (served by ux instructors.user_id and ix assignments.instructor_id).
        var groups = await (
                from instructor in db.Instructors.AsNoTracking()
                join assignment in db.CourseOfferingInstructors.AsNoTracking() on instructor.Id equals assignment.InstructorId
                join offering in db.CourseOfferings.AsNoTracking() on assignment.CourseOfferingId equals offering.Id
                join course in db.Courses.AsNoTracking() on offering.CourseId equals course.Id
                where instructor.UserId == teacherUserId
                orderby course.Title, offering.Name
                select new
                {
                    OfferingId = offering.Id, CourseId = course.Id, CourseTitle = course.Title, OfferingName = offering.Name,
                    IsPublished = offering.Status == CourseOfferingStatus.Published, offering.MaximumCapacity
                })
            .ToListAsync(cancellationToken);
        if (groups.Count == 0) return [];

        // 2) The classes of those groups (a group has a few dozen at most): count and next class are computed here.
        var ids = groups.Select(group => group.OfferingId).ToArray();
        var now = timeProvider.GetUtcNow();
        var sessions = (await db.OfferingSessions.AsNoTracking()
                .Where(session => ids.Contains(session.CourseOfferingId))
                .Select(session => new { session.Id, session.CourseOfferingId, session.StartsAt, session.EndsAt })
                .ToListAsync(cancellationToken))
            .ToLookup(session => session.CourseOfferingId);

        return groups.Select(group =>
        {
            var own = sessions[group.OfferingId];
            var next = own.Where(session => session.EndsAt > now).OrderBy(session => session.StartsAt).ThenBy(session => session.Id)
                .FirstOrDefault();
            return new TeacherGroup(group.OfferingId, group.CourseId, group.CourseTitle, group.OfferingName, group.IsPublished,
                group.MaximumCapacity, own.Count(), next?.Id, next?.StartsAt);
        }).ToArray();
    }

    public async Task<IReadOnlyList<GroupSession>?> GetGroupSessionsAsync(Guid teacherUserId, Guid offeringId,
        CancellationToken cancellationToken)
    {
        var assigned = await (
                from instructor in db.Instructors.AsNoTracking()
                join assignment in db.CourseOfferingInstructors.AsNoTracking() on instructor.Id equals assignment.InstructorId
                where instructor.UserId == teacherUserId && assignment.CourseOfferingId == offeringId
                select assignment.CourseOfferingId)
            .AnyAsync(cancellationToken);
        if (!assigned) return null;

        return await db.OfferingSessions.AsNoTracking()
            .Where(session => session.CourseOfferingId == offeringId)
            .OrderBy(session => session.StartsAt).ThenBy(session => session.Id)
            .Select(session => new GroupSession(session.Id, session.StartsAt, session.EndsAt, session.VirtualLink))
            .ToListAsync(cancellationToken);
    }

    public Task<LessonRef?> GetLessonAsync(Guid lessonId, CancellationToken cancellationToken) =>
        db.Lessons.AsNoTracking()
            .Where(lesson => lesson.Id == lessonId)
            .Select(lesson => new LessonRef(lesson.Id, lesson.CourseId, lesson.Title, lesson.IsPublished))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LessonRef>> GetPublishedLessonsAsync(Guid courseId, CancellationToken cancellationToken) =>
        await (
                from lesson in db.Lessons.AsNoTracking()
                join unit in db.CourseUnits.AsNoTracking() on lesson.UnitId equals unit.Id
                where lesson.CourseId == courseId && lesson.IsPublished
                orderby unit.Position, unit.CreatedAt, lesson.Position, lesson.CreatedAt
                select new LessonRef(lesson.Id, lesson.CourseId, lesson.Title, lesson.IsPublished))
            .ToListAsync(cancellationToken);
}
