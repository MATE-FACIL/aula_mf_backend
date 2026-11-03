using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Domain.Offerings;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

public sealed class CourseOfferingRepository(CoursesDbContext dbContext) : ICourseOfferingRepository
{
    public Task<bool> CourseExistsAsync(Guid courseId, CancellationToken cancellationToken) =>
        dbContext.Courses.AnyAsync(course => course.Id == courseId, cancellationToken);

    public Task<CourseOffering?> GetAsync(Guid offeringId, CancellationToken cancellationToken) =>
        dbContext.CourseOfferings.Include(offering => offering.Sessions)
            .Include(offering => offering.InstructorAssignments)
            .Include(offering => offering.SpecificPromotion)
            .AsSplitQuery()
            .SingleOrDefaultAsync(offering => offering.Id == offeringId, cancellationToken);

    public async Task<IReadOnlyList<CourseOffering>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        await dbContext.CourseOfferings.AsNoTracking().Include(offering => offering.Sessions)
            .Include(offering => offering.InstructorAssignments)
            .Include(offering => offering.SpecificPromotion)
            .AsSplitQuery()
            .Where(offering => offering.CourseId == courseId).OrderBy(offering => offering.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PublishedOfferingSummary>> ListPubliclyVisibleByCourseAsync(
        Guid courseId, CancellationToken cancellationToken) =>
        await dbContext.CourseOfferings.AsNoTracking()
            .Join(dbContext.Courses.Where(course => course.IsPublished),
                offering => offering.CourseId, course => course.Id, (offering, course) => offering)
            .Where(offering => offering.CourseId == courseId &&
                offering.Status == CourseOfferingStatus.Published && offering.CurrencyCode != null)
            .Select(offering => new PublishedOfferingSummary(offering.Id, offering.CourseId,
                offering.Name, offering.BasePrice, offering.CurrencyCode))
            .ToListAsync(cancellationToken);

    public void Add(CourseOffering offering) => dbContext.CourseOfferings.Add(offering);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
