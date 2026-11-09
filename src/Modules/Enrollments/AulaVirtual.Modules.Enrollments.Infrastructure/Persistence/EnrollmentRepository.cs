using System.Data;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;

public sealed class EnrollmentRepository(EnrollmentsDbContext db) : IEnrollmentRepository, IEnrollmentRoster
{
    // Served by ix_enrollments_offering_status.
    public async Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid courseOfferingId,
        CancellationToken cancellationToken) =>
        await db.Enrollments.AsNoTracking()
            .Where(item => item.CourseOfferingId == courseOfferingId && item.Status == EnrollmentStatus.Active)
            .Select(item => item.StudentUserId)
            .ToListAsync(cancellationToken);

    public Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        db.Enrollments.SingleOrDefaultAsync(item => item.Id == enrollmentId, cancellationToken);

    public async Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query,
        CancellationToken cancellationToken)
    {
        var items = db.Enrollments.AsNoTracking();
        if (query.CourseOfferingId is { } offeringId) items = items.Where(item => item.CourseOfferingId == offeringId);
        if (query.StudentUserId is { } studentId) items = items.Where(item => item.StudentUserId == studentId);
        if (query.Status is { } status) items = items.Where(item => item.Status == status);

        var total = await items.CountAsync(cancellationToken);
        var skip = (query.Page - 1) * query.PageSize;
        if (total == 0 || skip >= total) return ([], total);

        var page = await items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip(skip).Take(query.PageSize).ToListAsync(cancellationToken);
        return (page, total);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds,
        CancellationToken cancellationToken)
    {
        var live = db.Enrollments.AsNoTracking().Where(item => item.Status != EnrollmentStatus.Withdrawn);
        if (offeringIds is not null)
        {
            var ids = offeringIds.ToArray();
            live = live.Where(item => ids.Contains(item.CourseOfferingId));
        }

        return await live.GroupBy(item => item.CourseOfferingId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
    }

    public async Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
            cancellationToken);

        // Serializes «count seats + insert» per edition only (other editions are not blocked).
        // The lock is released automatically at commit or rollback.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({enrollment.CourseOfferingId.ToString()}, 0))",
            cancellationToken);

        var live = db.Enrollments.Where(item =>
            item.CourseOfferingId == enrollment.CourseOfferingId && item.Status != EnrollmentStatus.Withdrawn);

        if (await live.AnyAsync(item => item.StudentUserId == enrollment.StudentUserId, cancellationToken))
            return SeatReservation.AlreadyEnrolled;
        if (await live.CountAsync(cancellationToken) >= maximumCapacity)
            return SeatReservation.Full;

        db.Enrollments.Add(enrollment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Backstop for the partial unique index.
            db.Entry(enrollment).State = EntityState.Detached;
            return SeatReservation.AlreadyEnrolled;
        }

        await transaction.CommitAsync(cancellationToken);
        return SeatReservation.Added;
    }

    public async Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId,
        CancellationToken cancellationToken) =>
        await db.Enrollments.AsNoTracking()
            .Where(item => item.StudentUserId == studentUserId && item.Status == EnrollmentStatus.Active)
            .Select(item => item.CourseOfferingId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Enrollment>> GetLiveByStudentsAsync(IReadOnlyCollection<Guid> studentUserIds,
        CancellationToken cancellationToken)
    {
        if (studentUserIds.Count == 0) return [];
        var ids = studentUserIds.ToArray();
        return await db.Enrollments.AsNoTracking()
            .Where(item => ids.Contains(item.StudentUserId) && item.Status != EnrollmentStatus.Withdrawn)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
