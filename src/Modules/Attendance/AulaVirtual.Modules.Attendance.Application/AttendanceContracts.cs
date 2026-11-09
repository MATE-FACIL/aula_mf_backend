using AulaVirtual.Modules.Attendance.Domain;

namespace AulaVirtual.Modules.Attendance.Application;

/// <summary>One line of the roster a teacher fills in. <paramref name="Status"/>: present, late or absent.</summary>
public sealed record AttendanceEntry(Guid StudentUserId, string Status, string? Note);

/// <summary>
/// A student in a class. <paramref name="Status"/> is null while nobody has marked them. <paramref name="Enrolled"/>
/// is false for someone who was marked and later left the group: their history stays, they cannot be marked again.
/// </summary>
public sealed record AttendanceStudentRow(
    Guid StudentUserId,
    string FullName,
    string? Status,
    string? Note,
    DateTimeOffset? RecordedAt,
    bool Enrolled);

public sealed record SessionAttendanceView(
    Guid SessionId,
    Guid OfferingId,
    string CourseTitle,
    string OfferingName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    // Attendance opens shortly before the class starts.
    DateTimeOffset OpensAt,
    bool CanMark,
    IReadOnlyList<AttendanceStudentRow> Students);

/// <summary><paramref name="Rate"/>: percentage of marked classes attended (present or late); null with no classes.</summary>
public sealed record AttendanceSummary(int Present, int Late, int Absent, int Total, int? Rate)
{
    public static AttendanceSummary From(int present, int late, int absent)
    {
        var total = present + late + absent;
        return new AttendanceSummary(present, late, absent, total,
            total == 0 ? null : (int)Math.Round((present + late) * 100.0 / total, MidpointRounding.AwayFromZero));
    }
}

public sealed record AttendanceItem(
    Guid SessionId,
    string CourseTitle,
    string OfferingName,
    DateTimeOffset StartsAt,
    string Status,
    string? Note);

public sealed record StudentAttendanceReport(Guid StudentUserId, AttendanceSummary Summary, IReadOnlyList<AttendanceItem> Recent);

public sealed record StatusCount(Guid StudentUserId, AttendanceStatus Status, int Count);

public interface IAttendanceRepository
{
    Task<IReadOnlyList<AttendanceRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Tracked, ready to change.</summary>
    Task<IReadOnlyList<AttendanceRecord>> GetTrackedBySessionAsync(Guid sessionId, CancellationToken cancellationToken);

    void Add(AttendanceRecord record);

    /// <summary>
    /// False when someone else marked one of these students at the same time (unique class + student). The
    /// tracked changes are discarded so the caller can read again and retry.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Counts per student and status, all time, in one grouped query.</summary>
    Task<IReadOnlyList<StatusCount>> CountByStatusAsync(IReadOnlyCollection<Guid> studentUserIds,
        CancellationToken cancellationToken);

    /// <summary>Marks per class (present, late, absent), in one grouped query.</summary>
    Task<IReadOnlyDictionary<Guid, (int Present, int Late, int Absent)>> CountBySessionsAsync(IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken);

    /// <summary>Marks per student and status inside one group, in one grouped query.</summary>
    Task<IReadOnlyList<StatusCount>> CountByStudentInOfferingAsync(Guid courseOfferingId, CancellationToken cancellationToken);

    /// <summary>Newest first, classes since <paramref name="since"/>, at most <paramref name="limit"/> rows.</summary>
    Task<IReadOnlyList<AttendanceRecord>> GetRecentAsync(IReadOnlyCollection<Guid> studentUserIds, DateTimeOffset since,
        int limit, CancellationToken cancellationToken);
}

public enum AttendanceResultStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
    Conflict
}

public sealed record AttendanceResult<T>(AttendanceResultStatus Status, T? Value, string? Detail)
{
    public static AttendanceResult<T> Ok(T value) => new(AttendanceResultStatus.Ok, value, null);
    public static AttendanceResult<T> NotFound(string detail) => new(AttendanceResultStatus.NotFound, default, detail);
    public static AttendanceResult<T> Forbidden(string detail) => new(AttendanceResultStatus.Forbidden, default, detail);
    public static AttendanceResult<T> Invalid(string detail) => new(AttendanceResultStatus.Invalid, default, detail);
    public static AttendanceResult<T> Conflict(string detail) => new(AttendanceResultStatus.Conflict, default, detail);
}
