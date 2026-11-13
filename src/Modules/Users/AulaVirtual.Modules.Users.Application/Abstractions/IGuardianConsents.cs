namespace AulaVirtual.Modules.Users.Application.Abstractions;

/// <summary>One acceptance: who accepted which version for which student, and when.</summary>
public sealed record ConsentRecord(Guid StudentUserId, Guid GuardianUserId, string TermsVersion, DateTimeOffset AcceptedAt);

// Consent of the parents for their children's data (SCRUM-38).
public interface IGuardianConsents
{
    /// <summary>
    /// The acceptances of this version for these students made by guardians who are STILL linked to them, in one
    /// query. A guardian who was unlinked no longer counts (nor does an acceptance of another version).
    /// </summary>
    Task<IReadOnlyList<ConsentRecord>> GetAcceptedAsync(IReadOnlyCollection<Guid> studentUserIds, string termsVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the guardian's acceptance for each of these students they are linked to, in ONE statement. Repeating it
    /// changes nothing. Returns how many acceptances were new.
    /// </summary>
    Task<int> AcceptAsync(Guid guardianUserId, IReadOnlyCollection<Guid> studentUserIds, string termsVersion,
        DateTimeOffset acceptedAt, CancellationToken cancellationToken);
}
