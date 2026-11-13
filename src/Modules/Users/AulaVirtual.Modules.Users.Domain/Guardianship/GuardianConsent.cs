namespace AulaVirtual.Modules.Users.Domain.Guardianship;

// The consent a parent or guardian gives for a student's data (SCRUM-38): which text they accepted (its version)
// and when. One row per guardian, student and version, so the history is kept: when the text changes, the new
// version needs a new acceptance and the old one stays as evidence of what was accepted at the time.
//
// Rows are inserted by IGuardianConsents.AcceptAsync in a single statement that also checks the guardian link, so
// two requests at once cannot duplicate one and nobody can accept for a student who is not theirs. The database
// rules (unique guardian + student + version, and a guardian that is not the student) back that up.
public sealed class GuardianConsent
{
    public const int MaxVersionLength = 60;

    private GuardianConsent()
    {
    }

    public Guid Id { get; private set; }

    public Guid GuardianUserId { get; private set; }

    public Guid StudentUserId { get; private set; }

    /// <summary>The version of the text that was accepted, e.g. <c>2026-11-provisional</c>.</summary>
    public string TermsVersion { get; private set; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; private set; }
}
