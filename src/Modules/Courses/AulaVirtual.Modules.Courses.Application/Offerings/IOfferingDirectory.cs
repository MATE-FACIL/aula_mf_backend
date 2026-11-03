namespace AulaVirtual.Modules.Courses.Application.Offerings;

public sealed record OfferingSummary(
    Guid Id,
    Guid CourseId,
    string CourseTitle,
    string Name,
    int MaximumCapacity,
    bool IsPublished);

// Cross-module read contract: what Enrollments needs to know about an edition (group),
// without access to Courses persistence.
public interface IOfferingDirectory
{
    Task<OfferingSummary?> GetAsync(Guid offeringId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OfferingSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> offeringIds,
        CancellationToken cancellationToken);

    /// <summary>Every edition, ordered by course and name (back-office pickers and rosters).</summary>
    Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken);
}
