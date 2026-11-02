using AulaVirtual.Modules.Auth.Domain.Accounts;

namespace AulaVirtual.Modules.Auth.Application.Abstractions;

public sealed record AccountLogin(LoginType LoginType, string LoginIdentifier);

public sealed record AccountSummary(
    Guid UserId,
    Guid AuthAccountId,
    AccountStatus Status,
    IReadOnlyList<AccountLogin> Logins,
    DateTimeOffset? LastLoginAt);

// Back-office access to accounts, keyed by UserId (the identifier other modules know).
// Reads are batched so a page of N users costs one query, not N.
public interface IAccountAdministration
{
    Task<IReadOnlyDictionary<Guid, AccountSummary>> GetByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetUserIdsByStatusAsync(
        AccountStatus status,
        CancellationToken cancellationToken);

    /// <summary>Tracked account with its sessions and refresh tokens, ready to be deactivated.</summary>
    Task<AuthAccount?> FindForStatusChangeAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
