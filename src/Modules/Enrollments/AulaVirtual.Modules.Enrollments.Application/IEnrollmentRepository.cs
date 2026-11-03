using AulaVirtual.Modules.Enrollments.Domain;

namespace AulaVirtual.Modules.Enrollments.Application;

public sealed record EnrollmentQuery(
    Guid? CourseOfferingId,
    Guid? StudentUserId,
    EnrollmentStatus? Status,
    int Page,
    int PageSize);

public enum SeatReservation
{
    Added,
    // The student already holds a seat (pending or active) in this edition.
    AlreadyEnrolled,
    // Every seat is taken by pending or active enrollments.
    Full
}

public interface IEnrollmentRepository
{
    /// <summary>Tracked, ready to change status.</summary>
    Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>Newest first, paged in the database.</summary>
    Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query,
        CancellationToken cancellationToken);

    /// <summary>Seats taken (pending + active) per edition; all editions when <paramref name="offeringIds"/> is null.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds the enrollment only if the student has no seat in the edition and a seat is free.
    /// Check and insert happen atomically per edition, so concurrent requests cannot exceed the capacity.
    /// </summary>
    Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity,
        CancellationToken cancellationToken);

    /// <summary>Editions where the student has an ACTIVE enrollment (served by the student/offering index).</summary>
    Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
