using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Auth.Domain.Sessions;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Authorization.Domain;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Application.Users.CreateUser;
using Role = AulaVirtual.Modules.Authorization.Domain.Role;

namespace AulaVirtual.Modules.Administration.Tests;

// In-memory stand-in for the Users, Auth and Authorization modules, so the administration
// use cases can be exercised end to end without PostgreSQL.
internal sealed class FakeBackOffice :
    IUserDirectory, IGuardianLinks, IGuardianConsents, ICreateUser,
    IAccountAdministration, IAuthRepository,
    IRoleDirectory, IAuthorizationRepository, IUserExistence, IPermissionChecker,
    IPasswordGenerator, IPasswordHasher
{
    private readonly Dictionary<string, Role> roles = Roles.All.ToDictionary(
        code => code, code => Role.Create(Guid.NewGuid(), code, code));

    private int nextDocument = 70_000_000;

    public List<UserSummary> People { get; } = [];
    public HashSet<(Guid Guardian, Guid Student)> Links { get; } = [];
    public List<ConsentRecord> Consents { get; } = [];
    public List<AuthAccount> Accounts { get; } = [];
    public List<UserRole> UserRoles { get; } = [];

    public Guid AddPerson(string givenNames, string surname, string? role = null,
        AccountStatus? status = AccountStatus.Active, DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        People.Add(new UserSummary(id, givenNames, surname, null, null, "DNI",
            (nextDocument++).ToString(), createdAt ?? DateTimeOffset.UtcNow, 0));
        if (role is not null) UserRoles.Add(UserRole.Create(id, roles[role].Id));
        if (status is not null)
        {
            var account = AuthAccount.Create(Guid.NewGuid(), id, "hash", DateTimeOffset.UtcNow);
            account.AddIdentifier(Guid.NewGuid(), LoginType.Email,
                $"{givenNames}@example.com".ToLowerInvariant(), DateTimeOffset.UtcNow);
            if (status == AccountStatus.Inactive) account.Deactivate(DateTimeOffset.UtcNow);
            Accounts.Add(account);
        }
        return id;
    }

    // ── Users ──────────────────────────────────────────────────────────────
    public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        var matches = People
            .Where(person => query.RestrictToIds is null || query.RestrictToIds.Contains(person.Id))
            .Where(person => string.IsNullOrWhiteSpace(query.Text) ||
                person.FullName.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(person => person.CreatedAt)
            .Select(WithLinkCount)
            .ToList();
        return Task.FromResult(new UserSearchPage(
            matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), matches.Count));
    }

    public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(People.Where(person => person.Id == userId).Select(WithLinkCount).SingleOrDefault());

    public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(People.Where(person => userIds.Contains(person.Id))
            .Select(WithLinkCount).ToList());

    public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) =>
        Task.FromResult(People.Count(person => createdSince is null || person.CreatedAt >= createdSince));

    // ── Parents' consent (same rules as the store: only linked guardians count, repeating changes nothing) ──
    public Task<IReadOnlyList<ConsentRecord>> GetAcceptedAsync(IReadOnlyCollection<Guid> studentUserIds,
        string termsVersion, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConsentRecord>>(Consents
            .Where(consent => studentUserIds.Contains(consent.StudentUserId) && consent.TermsVersion == termsVersion &&
                Links.Contains((consent.GuardianUserId, consent.StudentUserId)))
            .ToList());

    public Task<int> AcceptAsync(Guid guardianUserId, IReadOnlyCollection<Guid> studentUserIds, string termsVersion,
        DateTimeOffset acceptedAt, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var studentId in studentUserIds.Where(id => Links.Contains((guardianUserId, id))))
        {
            if (Consents.Any(consent => consent.GuardianUserId == guardianUserId && consent.StudentUserId == studentId &&
                consent.TermsVersion == termsVersion)) continue;
            Consents.Add(new ConsentRecord(studentId, guardianUserId, termsVersion, acceptedAt));
            added++;
        }

        return Task.FromResult(added);
    }

    public Task<bool> LinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Links.Add((guardianUserId, studentUserId)));

    public Task<bool> UnlinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Links.Remove((guardianUserId, studentUserId)));

    public Task<IReadOnlyList<Guid>> GetStudentIdsAsync(Guid guardianUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Links.Where(link => link.Guardian == guardianUserId)
            .Select(link => link.Student).ToList());

    public Task<IReadOnlyList<Guid>> GetGuardianIdsAsync(Guid studentUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Links.Where(link => link.Student == studentUserId)
            .Select(link => link.Guardian).ToList());

    public Task<CreateUserResult> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (command.DocumentNumber is not null &&
            People.Any(person => person.DocumentNumber == command.DocumentNumber))
        {
            return Task.FromResult(new CreateUserResult(CreateUserStatus.DuplicateDocument));
        }

        var id = Guid.NewGuid();
        People.Add(new UserSummary(id, command.GivenNames, command.FirstSurname, command.SecondSurname,
            command.Email, command.DocumentType, command.DocumentNumber, DateTimeOffset.UtcNow, 0));
        return Task.FromResult(new CreateUserResult(CreateUserStatus.Created, id));
    }

    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(People.Any(person => person.Id == userId));

    private UserSummary WithLinkCount(UserSummary person) => person with
    {
        LinkCount = Links.Count(link => link.Guardian == person.Id || link.Student == person.Id)
    };

    // ── Auth ───────────────────────────────────────────────────────────────
    public Task<IReadOnlyDictionary<Guid, AccountSummary>> GetByUserIdsAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, AccountSummary>>(Accounts
            .Where(account => userIds.Contains(account.UserId))
            .ToDictionary(account => account.UserId, account => new AccountSummary(account.UserId, account.Id,
                account.Status, account.Identifiers.Select(identifier =>
                    new AccountLogin(identifier.LoginType, identifier.LoginIdentifier)).ToList(), account.LastLoginAt)));

    public Task<IReadOnlyList<Guid>> GetUserIdsByStatusAsync(AccountStatus status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Accounts.Where(account => account.Status == status)
            .Select(account => account.UserId).ToList());

    public Task<AuthAccount?> FindForStatusChangeAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.SingleOrDefault(account => account.UserId == userId));

    public Task<AuthAccount?> FindAccountByIdentifierAsync(LoginType loginType, string normalizedLoginIdentifier,
        CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.SingleOrDefault(account => account.Identifiers.Any(identifier =>
            identifier.LoginType == loginType && identifier.NormalizedLoginIdentifier == normalizedLoginIdentifier)));

    public Task<AuthAccount?> FindAccountByIdAsync(Guid authAccountId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.SingleOrDefault(account => account.Id == authAccountId));

    public Task<RefreshTokenContext?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AuthSession>> GetSessionsAsync(Guid authAccountId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public void AddAccount(AuthAccount account) => Accounts.Add(account);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public string Generate() => "Generated-Password-1";

    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string encodedHash) => encodedHash == Hash(password);

    // ── Authorization ──────────────────────────────────────────────────────
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(UserRoles
            .Where(userRole => userIds.Contains(userRole.UserId))
            .GroupBy(userRole => userRole.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group
                .Select(userRole => CodeOf(userRole.RoleId)).Order().ToList()));

    public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(UserRoles.Where(userRole => CodeOf(userRole.RoleId) == roleCode)
            .Select(userRole => userRole.UserId).Distinct().ToList());

    public Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(UserRoles.GroupBy(userRole => CodeOf(userRole.RoleId))
            .ToDictionary(group => group.Key, group => group.Count()));

    public Task<Role?> GetRoleByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(roles.GetValueOrDefault(code));

    public Task<bool> UserHasRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) =>
        Task.FromResult(UserRoles.Any(userRole => userRole.UserId == userId && userRole.RoleId == roleId));

    public void Add(UserRole userRole) => UserRoles.Add(userRole);

    public Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    private string CodeOf(Guid roleId) => roles.Values.Single(role => role.Id == roleId).Code;

    public Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<CatalogVersion?> GetCatalogVersionAsync(string code, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<bool> TryAssignFirstAdministratorAsync(Guid userId, Guid administratorRoleId,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> RoleHasPermissionAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetRoleCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public void Add(Role role) => throw new NotSupportedException();
    public void Add(Permission permission) => throw new NotSupportedException();
    public void Add(RolePermission rolePermission) => throw new NotSupportedException();
    public void Add(CatalogVersion version) => throw new NotSupportedException();
    public Task RemoveUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task RemoveRolePermissionAsync(Guid roleId, Guid permissionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
