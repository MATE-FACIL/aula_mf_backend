namespace AulaVirtual.Modules.Users.Domain.Guardianship;

// A parent or legal guardian who follows a student. Both ends are users of this module;
// whether each one holds the guardian/student role is checked by the caller (Authorization).
public sealed class GuardianLink
{
    private GuardianLink()
    {
    }

    private GuardianLink(Guid guardianUserId, Guid studentUserId, DateTimeOffset createdAt)
    {
        GuardianUserId = guardianUserId;
        StudentUserId = studentUserId;
        CreatedAt = createdAt;
    }

    public Guid GuardianUserId { get; private set; }

    public Guid StudentUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static GuardianLink Create(Guid guardianUserId, Guid studentUserId, DateTimeOffset createdAt)
    {
        if (guardianUserId == Guid.Empty)
        {
            throw new ArgumentException("A guardian identifier is required.", nameof(guardianUserId));
        }

        if (studentUserId == Guid.Empty)
        {
            throw new ArgumentException("A student identifier is required.", nameof(studentUserId));
        }

        if (guardianUserId == studentUserId)
        {
            throw new ArgumentException("A user cannot be their own guardian.", nameof(studentUserId));
        }

        return new GuardianLink(guardianUserId, studentUserId, createdAt);
    }
}
