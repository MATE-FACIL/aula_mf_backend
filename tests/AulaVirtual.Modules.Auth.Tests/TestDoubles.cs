using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Auth.Domain.Sessions;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Auth.Tests;

internal class FakeAuthRepository : IAuthRepository
{
    public List<AuthAccount> Accounts { get; } = [];

    public int SaveChangesCalls { get; private set; }

    public Task<AuthAccount?> FindAccountByIdentifierAsync(
        LoginType loginType,
        string normalizedLoginIdentifier,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Accounts.SingleOrDefault(
            account =>
                account.Identifiers.Any(identifier =>
                    identifier.LoginType == loginType &&
                    identifier.NormalizedLoginIdentifier ==
                        normalizedLoginIdentifier)));
    }

    public Task<AuthAccount?> FindAccountByIdAsync(
        Guid authAccountId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Accounts.SingleOrDefault(
            account => account.Id == authAccountId));
    }

    public Task<RefreshTokenContext?> FindRefreshTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        foreach (var account in Accounts)
        {
            foreach (var session in account.Sessions)
            {
                var token = session.RefreshTokens.SingleOrDefault(
                    candidate => candidate.TokenHash == tokenHash);

                if (token is not null)
                {
                    return Task.FromResult<RefreshTokenContext?>(
                        new RefreshTokenContext(token, session, account));
                }
            }
        }

        return Task.FromResult<RefreshTokenContext?>(null);
    }

    public Task<IReadOnlyList<AuthSession>> GetSessionsAsync(
        Guid authAccountId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthSession> sessions = Accounts
            .Single(account => account.Id == authAccountId)
            .Sessions
            .ToList();

        return Task.FromResult(sessions);
    }

    public virtual void AddAccount(AuthAccount account)
    {
        Accounts.Add(account);
    }

    public virtual Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class StubPasswordHasher(string validPassword)
    : IPasswordHasher
{
    public string Hash(string password) => "encoded";

    public bool Verify(string password, string encodedHash)
    {
        return password == validPassword;
    }
}

internal sealed class StubAccessTokenService : IAccessTokenService
{
    public AccessToken Generate(AuthAccount account)
    {
        return new AccessToken("access-token", "Bearer", 900);
    }
}

internal sealed class SequentialRefreshTokenService : IRefreshTokenService
{
    private int sequence = 1;

    public string Generate()
    {
        sequence++;
        return "refresh-token-" + sequence;
    }

    public string ComputeHash(string token)
    {
        return "hash:" + token;
    }
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow)
    : TimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan duration)
    {
        UtcNow = UtcNow.Add(duration);
    }
}

internal sealed class FakeRoleDirectory : IRoleDirectory
{
    public Dictionary<Guid, IReadOnlyList<string>> RolesByUser { get; } = [];

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(RolesByUser
            .Where(entry => userIds.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value));

    public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(RolesByUser
            .Where(entry => entry.Value.Contains(roleCode))
            .Select(entry => entry.Key)
            .ToArray());

    public Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(RolesByUser
            .SelectMany(entry => entry.Value)
            .GroupBy(code => code)
            .ToDictionary(group => group.Key, group => group.Count()));
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    public List<UserSummary> Users { get; } = [];

    public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        var matches = Users
            .Where(user => query.RestrictToIds is null || query.RestrictToIds.Contains(user.Id))
            .OrderByDescending(user => user.CreatedAt)
            .ToList();
        return Task.FromResult(new UserSearchPage(
            matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
            matches.Count));
    }

    public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

    public Task<IReadOnlyList<UserSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(Users.Where(user => userIds.Contains(user.Id)).ToList());

    public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Count(user => createdSince is null || user.CreatedAt >= createdSince));
}
