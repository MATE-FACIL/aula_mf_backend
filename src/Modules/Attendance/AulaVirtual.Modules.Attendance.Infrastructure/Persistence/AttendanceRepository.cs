using AulaVirtual.Modules.Attendance.Application;
using AulaVirtual.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AulaVirtual.Modules.Attendance.Infrastructure.Persistence;

public sealed class AttendanceRepository(AttendanceDbContext db) : IAttendanceRepository
{
    public async Task<IReadOnlyList<AttendanceRecord>> GetBySessionAsync(Guid sessionId,
        CancellationToken cancellationToken) =>
        await db.Records.AsNoTracking().Where(record => record.SessionId == sessionId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceRecord>> GetTrackedBySessionAsync(Guid sessionId,
        CancellationToken cancellationToken) =>
        await db.Records.Where(record => record.SessionId == sessionId).ToListAsync(cancellationToken);

    public void Add(AttendanceRecord record) => db.Records.Add(record);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                   {
                                                       SqlState: PostgresErrorCodes.UniqueViolation
                                                   })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<StatusCount>> CountByStatusAsync(IReadOnlyCollection<Guid> studentUserIds,
        CancellationToken cancellationToken)
    {
        if (studentUserIds.Count == 0) return [];
        var ids = studentUserIds.ToArray();
        var rows = await db.Records.AsNoTracking()
            .Where(record => ids.Contains(record.StudentUserId))
            .GroupBy(record => new { record.StudentUserId, record.Status })
            .Select(group => new { group.Key.StudentUserId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new StatusCount(row.StudentUserId, row.Status, row.Count)).ToArray();
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetRecentAsync(IReadOnlyCollection<Guid> studentUserIds,
        DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        if (studentUserIds.Count == 0 || limit <= 0) return [];
        var ids = studentUserIds.ToArray();
        return await db.Records.AsNoTracking()
            .Where(record => ids.Contains(record.StudentUserId) && record.SessionStartsAt >= since)
            .OrderByDescending(record => record.SessionStartsAt).ThenBy(record => record.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
