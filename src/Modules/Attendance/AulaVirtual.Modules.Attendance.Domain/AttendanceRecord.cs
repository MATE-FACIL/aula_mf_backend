namespace AulaVirtual.Modules.Attendance.Domain;

public enum AttendanceStatus
{
    Present,
    Late,
    Absent
}

public static class AttendanceStatusCodes
{
    public const string Present = "present";
    public const string Late = "late";
    public const string Absent = "absent";

    public static string ToCode(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => Present,
        AttendanceStatus.Late => Late,
        AttendanceStatus.Absent => Absent,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static bool TryParse(string? code, out AttendanceStatus status)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case Present: status = AttendanceStatus.Present; return true;
            case Late: status = AttendanceStatus.Late; return true;
            case Absent: status = AttendanceStatus.Absent; return true;
            default: status = default; return false;
        }
    }
}

// Whether a student came to one class (SCRUM-53). One record per class and student; a correction changes
// the same record and remembers who made it. The class start is copied here because the schedule of a
// published group never changes, so a student's history is ordered by an index without asking Courses.
public sealed class AttendanceRecord
{
    public const int MaxNoteLength = 200;

    private AttendanceRecord()
    {
    }

    private AttendanceRecord(Guid id, Guid sessionId, Guid courseOfferingId, DateTimeOffset sessionStartsAt,
        Guid studentUserId, AttendanceStatus status, string? note, Guid recordedByUserId, DateTimeOffset recordedAt)
    {
        Id = id;
        SessionId = sessionId;
        CourseOfferingId = courseOfferingId;
        SessionStartsAt = sessionStartsAt;
        StudentUserId = studentUserId;
        Status = status;
        Note = note;
        RecordedByUserId = recordedByUserId;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid CourseOfferingId { get; private set; }
    public DateTimeOffset SessionStartsAt { get; private set; }
    public Guid StudentUserId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    /// <summary>Optional, e.g. «Entró 10 min tarde» or «Avisó que estaba enfermo». Visible to the family.</summary>
    public string? Note { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static AttendanceRecord Create(Guid id, Guid sessionId, Guid courseOfferingId,
        DateTimeOffset sessionStartsAt, Guid studentUserId, AttendanceStatus status, string? note,
        Guid recordedByUserId, DateTimeOffset recordedAt)
    {
        RequireId(id, nameof(id));
        RequireId(sessionId, nameof(sessionId));
        RequireId(courseOfferingId, nameof(courseOfferingId));
        RequireId(studentUserId, nameof(studentUserId));
        RequireId(recordedByUserId, nameof(recordedByUserId));
        return new AttendanceRecord(id, sessionId, courseOfferingId, sessionStartsAt, studentUserId,
            RequireStatus(status), CleanNote(note), recordedByUserId, recordedAt);
    }

    /// <summary>True when something actually changed (a resubmitted roster does not touch unchanged rows).</summary>
    public bool Change(AttendanceStatus status, string? note, Guid actorUserId, DateTimeOffset now)
    {
        RequireId(actorUserId, nameof(actorUserId));
        var cleanNote = CleanNote(note);
        if (Status == RequireStatus(status) && Note == cleanNote) return false;

        Status = status;
        Note = cleanNote;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        return true;
    }

    public static string? CleanNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > MaxNoteLength)
            throw new ArgumentOutOfRangeException(nameof(note), $"La nota admite como máximo {MaxNoteLength} caracteres.");
        return trimmed;
    }

    private static AttendanceStatus RequireStatus(AttendanceStatus status) =>
        Enum.IsDefined(status) ? status : throw new ArgumentOutOfRangeException(nameof(status));

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", name);
    }
}
