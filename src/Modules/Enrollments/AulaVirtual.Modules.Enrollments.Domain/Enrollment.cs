namespace AulaVirtual.Modules.Enrollments.Domain;

public enum EnrollmentStatus
{
    // Registered by an administrator; waiting for payment confirmation.
    Pending,
    // Payment confirmed: the student takes part in the edition.
    Active,
    // Left the edition. Kept for history; frees the seat.
    Withdrawn
}

// A student's seat in a course edition (group). Activation is manual: an administrator confirms
// the payment and activates it, and every step records who did it and when.
public sealed class Enrollment
{
    public const int MaxPaymentNoteLength = 300;

    private Enrollment()
    {
    }

    private Enrollment(Guid id, Guid studentUserId, Guid courseOfferingId, string? paymentNote,
        Guid createdByUserId, DateTimeOffset createdAt)
    {
        Id = id;
        StudentUserId = studentUserId;
        CourseOfferingId = courseOfferingId;
        PaymentNote = paymentNote;
        Status = EnrollmentStatus.Pending;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid StudentUserId { get; private set; }

    public Guid CourseOfferingId { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    /// <summary>Free text such as «Yape 12/11, op. 123456». The payment rule is still being defined.</summary>
    public string? PaymentNote { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? ActivatedByUserId { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public Guid? WithdrawnByUserId { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool HoldsSeat => Status != EnrollmentStatus.Withdrawn;

    public static Enrollment Create(Guid id, Guid studentUserId, Guid courseOfferingId, string? paymentNote,
        Guid createdByUserId, DateTimeOffset createdAt)
    {
        RequireId(id, nameof(id));
        RequireId(studentUserId, nameof(studentUserId));
        RequireId(courseOfferingId, nameof(courseOfferingId));
        RequireId(createdByUserId, nameof(createdByUserId));
        return new Enrollment(id, studentUserId, courseOfferingId, CleanNote(paymentNote), createdByUserId, createdAt);
    }

    public void Activate(Guid actorUserId, string? paymentNote, DateTimeOffset now)
    {
        RequireId(actorUserId, nameof(actorUserId));
        if (Status != EnrollmentStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending enrollment can be activated.");
        }

        Status = EnrollmentStatus.Active;
        ActivatedByUserId = actorUserId;
        ActivatedAt = now;
        PaymentNote = CleanNote(paymentNote) ?? PaymentNote;
        UpdatedAt = now;
    }

    public void Withdraw(Guid actorUserId, DateTimeOffset now)
    {
        RequireId(actorUserId, nameof(actorUserId));
        if (Status == EnrollmentStatus.Withdrawn)
        {
            throw new InvalidOperationException("The enrollment is already withdrawn.");
        }

        Status = EnrollmentStatus.Withdrawn;
        WithdrawnByUserId = actorUserId;
        WithdrawnAt = now;
        UpdatedAt = now;
    }

    private static string? CleanNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > MaxPaymentNoteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(note), "The payment note is too long.");
        }

        return trimmed;
    }

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", name);
    }
}
