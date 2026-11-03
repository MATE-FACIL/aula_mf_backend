using System.Linq.Expressions;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Domain.Offerings;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

public sealed class OfferingDirectory(CoursesDbContext dbContext) : IOfferingDirectory
{
    public Task<OfferingSummary?> GetAsync(Guid offeringId, CancellationToken cancellationToken) =>
        Summaries(offering => offering.Id == offeringId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<OfferingSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> offeringIds,
        CancellationToken cancellationToken)
    {
        if (offeringIds.Count == 0) return [];
        var ids = offeringIds.ToArray();
        return await Summaries(offering => ids.Contains(offering.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken) =>
        await Summaries(_ => true, ordered: true).ToListAsync(cancellationToken);

    // Filter and order on the entities, project LAST: EF cannot translate a Where/OrderBy over the
    // projected record. Projection only: no sessions, instructors or promotions are loaded.
    private IQueryable<OfferingSummary> Summaries(Expression<Func<CourseOffering, bool>> filter, bool ordered = false)
    {
        var rows = dbContext.CourseOfferings.AsNoTracking().Where(filter)
            .Join(dbContext.Courses.AsNoTracking(), offering => offering.CourseId, course => course.Id,
                (offering, course) => new { Offering = offering, CourseTitle = course.Title });

        if (ordered)
        {
            rows = rows.OrderBy(row => row.CourseTitle).ThenBy(row => row.Offering.Name);
        }

        return rows.Select(row => new OfferingSummary(row.Offering.Id, row.Offering.CourseId, row.CourseTitle,
            row.Offering.Name, row.Offering.MaximumCapacity, row.Offering.Status == CourseOfferingStatus.Published));
    }
}
