using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Auth.Infrastructure.Persistence;

public sealed class AccountAdministration(AuthDbContext dbContext) : IAccountAdministration
{
    public async Task<IReadOnlyDictionary<Guid, AccountSummary>> GetByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, AccountSummary>();
        }

        var ids = userIds.ToArray();
        var accounts = await dbContext.AuthAccounts
            .AsNoTracking()
            .Where(account => ids.Contains(account.UserId))
            .Select(account => new
            {
                account.Id,
                account.UserId,
                account.Status,
                account.LastLoginAt,
                Logins = account.Identifiers
                    .OrderBy(identifier => identifier.LoginType)
                    .Select(identifier => new AccountLogin(identifier.LoginType, identifier.LoginIdentifier))
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // One account per user today; if that ever changes, the most recent login wins.
        return accounts
            .GroupBy(account => account.UserId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var account = group.OrderByDescending(candidate => candidate.LastLoginAt).First();
                    return new AccountSummary(
                        account.UserId, account.Id, account.Status, account.Logins, account.LastLoginAt);
                });
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsByStatusAsync(
        AccountStatus status,
        CancellationToken cancellationToken) =>
        await dbContext.AuthAccounts
            .AsNoTracking()
            .Where(account => account.Status == status)
            .Select(account => account.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task<AuthAccount?> FindForStatusChangeAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AuthAccounts
            .Include(account => account.Sessions)
            .ThenInclude(session => session.RefreshTokens)
            .AsSplitQuery()
            .FirstOrDefaultAsync(account => account.UserId == userId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
