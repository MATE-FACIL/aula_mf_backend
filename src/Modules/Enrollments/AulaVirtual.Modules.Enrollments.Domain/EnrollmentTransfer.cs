namespace AulaVirtual.Modules.Enrollments.Domain;

// The record of a change of group (SCRUM-45): who moved the student, when, from which group to which, and why.
// The old seat stays as a withdrawn enrollment and the new one starts from it; this row ties them together so the
// student's path through the groups can be told from the database.
public sealed class EnrollmentTransfer
{
    public const int MaxReasonLength = 300;

    private EnrollmentTransfer()
    {
    }

    private EnrollmentTransfer(Guid id, Guid studentUserId, Guid fromEnrollmentId, Guid toEnrollmentId,
        Guid fromOfferingId, Guid toOfferingId, string? reason, Guid transferredByUserId, DateTimeOffset transferredAt)
    {
        Id = id;
        StudentUserId = studentUserId;
        FromEnrollmentId = fromEnrollmentId;
        ToEnrollmentId = toEnrollmentId;
        FromOfferingId = fromOfferingId;
        ToOfferingId = toOfferingId;
        Reason = reason;
        TransferredByUserId = transferredByUserId;
        TransferredAt = transferredAt;
    }

    public Guid Id { get; private set; }

    public Guid StudentUserId { get; private set; }

    public Guid FromEnrollmentId { get; private set; }

    public Guid ToEnrollmentId { get; private set; }

    public Guid FromOfferingId { get; private set; }

    public Guid ToOfferingId { get; private set; }

    /// <summary>Free text, e.g. «El diagnóstico confirmó Intermedio».</summary>
    public string? Reason { get; private set; }

    public Guid TransferredByUserId { get; private set; }

    public DateTimeOffset TransferredAt { get; private set; }

    public static EnrollmentTransfer Record(Guid id, Enrollment from, Enrollment to, string? reason, Guid transferredByUserId,
        DateTimeOffset transferredAt)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (id == Guid.Empty || transferredByUserId == Guid.Empty)
        {
            throw new ArgumentException("Identifiers are required.");
        }

        if (from.StudentUserId != to.StudentUserId)
        {
            throw new ArgumentException("Both seats must belong to the same student.", nameof(to));
        }

        if (from.CourseOfferingId == to.CourseOfferingId)
        {
            throw new ArgumentException("A transfer needs two different groups.", nameof(to));
        }

        var trimmed = reason?.Trim();
        if (trimmed?.Length > MaxReasonLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "The reason is too long.");
        }

        return new EnrollmentTransfer(id, from.StudentUserId, from.Id, to.Id, from.CourseOfferingId, to.CourseOfferingId,
            string.IsNullOrEmpty(trimmed) ? null : trimmed, transferredByUserId, transferredAt);
    }
}
