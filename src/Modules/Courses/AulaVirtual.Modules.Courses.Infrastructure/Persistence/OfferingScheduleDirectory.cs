using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Domain.Offerings;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

public sealed class OfferingScheduleDirectory(CoursesDbContext db) : IOfferingScheduleDirectory
{
    public async Task<IReadOnlyList<ScheduledSession>> GetSessionsAsync(
        IReadOnlyCollection<Guid> offeringIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        if (offeringIds.Count == 0 || limit <= 0 || from >= to) return [];
        var ids = offeringIds.ToArray();

        // 1) Sessions in range, with edition, course and (optional) category — one statement,
        //    served by ix_offering_sessions_offering_starts_at.
        var rows = await (
                from session in db.OfferingSessions.AsNoTracking()
                join offering in db.CourseOfferings.AsNoTracking() on session.CourseOfferingId equals offering.Id
                join course in db.Courses.AsNoTracking() on offering.CourseId equals course.Id
                join category in db.Categories.AsNoTracking() on course.CategoryId equals category.Id into categories
                from category in categories.DefaultIfEmpty()
                where ids.Contains(offering.Id) &&
                      offering.Status == CourseOfferingStatus.Published &&
                      session.StartsAt < to && session.EndsAt > @from
                orderby session.StartsAt, session.Id
                select new
                {
                    session.Id,
                    OfferingId = offering.Id,
                    OfferingName = offering.Name,
                    CourseTitle = course.Title,
                    CategoryName = category == null ? null : category.Name,
                    session.StartsAt,
                    session.EndsAt,
                    session.VirtualLink
                })
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return [];

        // 2) Teachers (as user ids) of only the editions that appear in the result.
        var resultOfferings = rows.Select(row => row.OfferingId).Distinct().ToArray();
        var teachers = (await (
                    from assignment in db.CourseOfferingInstructors.AsNoTracking()
                    join instructor in db.Instructors.AsNoTracking() on assignment.InstructorId equals instructor.Id
                    where resultOfferings.Contains(assignment.CourseOfferingId) && instructor.UserId != null
                    select new { assignment.CourseOfferingId, UserId = instructor.UserId!.Value })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.CourseOfferingId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(row => row.UserId).ToArray());

        return rows.Select(row => new ScheduledSession(row.Id, row.OfferingId, row.OfferingName, row.CourseTitle,
                row.CategoryName, row.StartsAt, row.EndsAt, row.VirtualLink,
                teachers.GetValueOrDefault(row.OfferingId) ?? []))
            .ToArray();
    }
}
