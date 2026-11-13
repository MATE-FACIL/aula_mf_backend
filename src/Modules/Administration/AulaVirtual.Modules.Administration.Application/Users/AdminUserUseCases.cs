using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Application.Accounts.ChangeStatus;
using AulaVirtual.Modules.Auth.Application.Accounts.CreateAccount;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Application.Consent;

namespace AulaVirtual.Modules.Administration.Application.Users;

// Back-office use cases for people: students, guardians (parents) and staff.
//
// Every module owns its own DbContext, so calls to DIFFERENT modules may run concurrently
// (Task.WhenAll), while calls to the SAME module stay sequential. Lists are paged in the
// database and enriched in batches: one page costs a fixed number of queries, never one per row.
//
// Creating a person spans three modules (Users → Auth → Authorization) without a shared
// transaction. Steps are ordered so a failure can only leave a person without a role, which an
// administrator can see in the list and fix; it never leaves an account without a person.
public sealed class AdminUserUseCases(
    IUserDirectory users,
    IGuardianLinks links,
    IGuardianConsents consents,
    IAccountAdministration accounts,
    IRoleDirectory roles,
    AuthorizationUseCases authorization,
    CreateStudentAccountHandler createStudentAccount,
    CreateAdministratorAccountHandler createEmailAccount,
    ChangeAccountStatusHandler changeAccountStatus,
    TimeProvider timeProvider)
{
    private const int RecentUsersCount = 5;
    private static readonly TimeSpan NewUserWindow = TimeSpan.FromDays(7);

    public async Task<AdminResult<AdminPage<AdminUserListItem>>> ListAsync(
        AdminUserListQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Role is not null && !Roles.All.Contains(query.Role))
        {
            return AdminResult<AdminPage<AdminUserListItem>>.Invalid($"Rol desconocido: «{query.Role}».");
        }

        if (!TryParseStatus(query.Status, out var status))
        {
            return AdminResult<AdminPage<AdminUserListItem>>.Invalid(
                $"Estado desconocido: «{query.Status}». Usa «active» o «inactive».");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, AdminUserListQuery.MaxPageSize);

        var roleIdsTask = query.Role is null
            ? Task.FromResult<IReadOnlyList<Guid>?>(null)
            : NullableAsync(roles.GetUserIdsInRoleAsync(query.Role, cancellationToken));
        var statusIdsTask = status is null
            ? Task.FromResult<IReadOnlyList<Guid>?>(null)
            : NullableAsync(accounts.GetUserIdsByStatusAsync(status.Value, cancellationToken));
        await Task.WhenAll(roleIdsTask, statusIdsTask);

        var restrictTo = Intersect(roleIdsTask.Result, statusIdsTask.Result);
        var result = await users.SearchAsync(
            new UserSearchQuery(query.Search, restrictTo, page, pageSize),
            cancellationToken);

        return AdminResult<AdminPage<AdminUserListItem>>.Ok(new AdminPage<AdminUserListItem>(
            await EnrichAsync(result.Items, cancellationToken), page, pageSize, result.Total));
    }

    public async Task<AdminSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var roleCountsTask = roles.CountUsersByRoleAsync(cancellationToken);

        var total = await users.CountAsync(null, cancellationToken);
        var recentCount = await users.CountAsync(timeProvider.GetUtcNow() - NewUserWindow, cancellationToken);
        var recent = await users.SearchAsync(
            new UserSearchQuery(null, null, 1, RecentUsersCount),
            cancellationToken);

        // EnrichAsync uses the Authorization module too: wait for its pending query first.
        var roleCounts = await roleCountsTask;
        var recentItems = await EnrichAsync(recent.Items, cancellationToken);

        return new AdminSummary(total, recentCount, roleCounts, recentItems);
    }

    public async Task<AdminUserDetail?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var person = await users.GetAsync(userId, cancellationToken);

        if (person is null)
        {
            return null;
        }

        // Authorization answers while Users is read: different modules, different DbContexts.
        var rolesTask = roles.GetRoleCodesByUserIdsAsync([userId], cancellationToken);
        var studentIds = await links.GetStudentIdsAsync(userId, cancellationToken);
        var guardianIds = await links.GetGuardianIdsAsync(userId, cancellationToken);
        var related = await users.GetManyAsync([.. studentIds, .. guardianIds], cancellationToken);
        var userRoles = (await rolesTask).GetValueOrDefault(userId) ?? [];

        // The accounts (Auth) and the consent (Users, again) run at the same time. Only a student has a consent.
        var accountsTask = accounts.GetByUserIdsAsync(
            [userId, .. related.Select(user => user.Id)],
            cancellationToken);
        var consentTask = userRoles.Contains(Roles.Student)
            ? ConsentOfAsync(userId, guardianIds, related, cancellationToken)
            : Task.FromResult<AdminConsent?>(null);
        await Task.WhenAll(accountsTask, consentTask);

        var accountsByUser = accountsTask.Result;
        var account = accountsByUser.GetValueOrDefault(userId);

        AdminLinkedUser ToLinked(UserSummary user)
        {
            var linkedAccount = accountsByUser.GetValueOrDefault(user.Id);
            return new AdminLinkedUser(user.Id, user.FullName, LoginOf(linkedAccount), StatusOf(linkedAccount));
        }

        var studentSet = studentIds.ToHashSet();
        var guardianSet = guardianIds.ToHashSet();

        return new AdminUserDetail(
            person.Id,
            person.GivenNames,
            person.FirstSurname,
            person.SecondSurname,
            person.FullName,
            person.Email,
            person.DocumentType,
            person.DocumentNumber,
            LoginOf(account),
            userRoles,
            StatusOf(account),
            person.CreatedAt,
            account?.LastLoginAt,
            related.Where(user => studentSet.Contains(user.Id)).Select(ToLinked).ToArray(),
            related.Where(user => guardianSet.Contains(user.Id)).Select(ToLinked).ToArray(),
            consentTask.Result);
    }

    /// <summary>One query: the acceptances of the current text by the guardians still linked to this student.</summary>
    private async Task<AdminConsent?> ConsentOfAsync(
        Guid studentId,
        IReadOnlyList<Guid> guardianIds,
        IReadOnlyList<UserSummary> related,
        CancellationToken cancellationToken)
    {
        var text = ConsentTexts.Current;
        if (guardianIds.Count == 0)
        {
            return new AdminConsent(AdminConsentStatus.NoGuardian, text.Version, text.IsProvisional, null, null);
        }

        var latest = (await consents.GetAcceptedAsync([studentId], text.Version, cancellationToken))
            .OrderByDescending(record => record.AcceptedAt)
            .FirstOrDefault();
        if (latest is null)
        {
            return new AdminConsent(AdminConsentStatus.Pending, text.Version, text.IsProvisional, null, null);
        }

        // The guardian who accepted is still linked, so they are among the people already loaded.
        var by = related.FirstOrDefault(user => user.Id == latest.GuardianUserId)?.FullName;
        return new AdminConsent(AdminConsentStatus.Accepted, text.Version, text.IsProvisional, latest.AcceptedAt, by);
    }

    public async Task<AdminResult<IssuedCredentials>> CreateStudentAsync(
        CreateStudentInput input,
        CancellationToken cancellationToken)
    {
        // Check the guardian BEFORE creating anything, so a bad id does not leave a half-created student.
        if (input.GuardianUserId is { } guardianId &&
            !await HasRoleAsync(guardianId, Roles.Guardian, cancellationToken))
        {
            return AdminResult<IssuedCredentials>.Invalid("El padre o tutor indicado no existe o no tiene ese rol.");
        }

        var result = await createStudentAccount.ExecuteAsync(
            new CreateStudentAccountCommand(
                input.GivenNames,
                input.FirstSurname,
                input.SecondSurname,
                input.Email,
                input.DocumentType,
                input.DocumentNumber),
            cancellationToken);

        var failure = ToFailure(result);

        if (failure is not null)
        {
            return failure;
        }

        var userId = result.UserId!.Value;
        await authorization.AssignRoleAsync(userId, Roles.Student, cancellationToken);

        if (input.GuardianUserId is { } linkTo)
        {
            await links.LinkAsync(linkTo, userId, cancellationToken);
        }

        return AdminResult<IssuedCredentials>.Ok(
            new IssuedCredentials(userId, result.LoginIdentifier!, result.InitialPassword!));
    }

    public async Task<AdminResult<IssuedCredentials>> CreateGuardianAsync(
        CreateGuardianInput input,
        CancellationToken cancellationToken)
    {
        // The «administrator» handler only creates an email sign-in account; it grants no role.
        // The role that defines what the person can do is assigned right after.
        var result = await createEmailAccount.ExecuteAsync(
            new CreateAdministratorAccountCommand(
                input.GivenNames,
                input.FirstSurname,
                input.SecondSurname,
                input.Email,
                input.DocumentType,
                input.DocumentNumber),
            cancellationToken);

        var failure = ToFailure(result);

        if (failure is not null)
        {
            return failure;
        }

        var userId = result.UserId!.Value;
        await authorization.AssignRoleAsync(userId, Roles.Guardian, cancellationToken);

        return AdminResult<IssuedCredentials>.Ok(
            new IssuedCredentials(userId, result.LoginIdentifier!, result.InitialPassword!));
    }

    public async Task<AdminResult<bool>> SetActiveAsync(
        Guid actorUserId,
        Guid userId,
        bool active,
        CancellationToken cancellationToken)
    {
        if (!active && actorUserId == userId)
        {
            return AdminResult<bool>.Invalid("No puedes desactivar tu propia cuenta.");
        }

        return await changeAccountStatus.ExecuteAsync(userId, active, cancellationToken) switch
        {
            ChangeAccountStatusResult.Changed => AdminResult<bool>.Ok(true),
            ChangeAccountStatusResult.Unchanged => AdminResult<bool>.Ok(false),
            ChangeAccountStatusResult.NotFound => AdminResult<bool>.NotFound("Esta persona no tiene cuenta de acceso."),
            _ => throw new InvalidOperationException("Unknown account status change result.")
        };
    }

    public async Task<AdminResult<bool>> LinkAsync(
        Guid guardianUserId,
        Guid studentUserId,
        CancellationToken cancellationToken)
    {
        var rolesByUser = await roles.GetRoleCodesByUserIdsAsync([guardianUserId, studentUserId], cancellationToken);

        if (!(rolesByUser.GetValueOrDefault(guardianUserId)?.Contains(Roles.Guardian) ?? false))
        {
            return AdminResult<bool>.Invalid("La primera persona no es padre o tutor.");
        }

        if (!(rolesByUser.GetValueOrDefault(studentUserId)?.Contains(Roles.Student) ?? false))
        {
            return AdminResult<bool>.Invalid("La persona a vincular no es alumno.");
        }

        return AdminResult<bool>.Ok(await links.LinkAsync(guardianUserId, studentUserId, cancellationToken));
    }

    public Task<bool> UnlinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) =>
        links.UnlinkAsync(guardianUserId, studentUserId, cancellationToken);

    private async Task<IReadOnlyList<AdminUserListItem>> EnrichAsync(
        IReadOnlyList<UserSummary> people,
        CancellationToken cancellationToken)
    {
        if (people.Count == 0)
        {
            return [];
        }

        var ids = people.Select(person => person.Id).ToArray();
        var accountsTask = accounts.GetByUserIdsAsync(ids, cancellationToken);
        var rolesTask = roles.GetRoleCodesByUserIdsAsync(ids, cancellationToken);
        await Task.WhenAll(accountsTask, rolesTask);

        return people.Select(person =>
        {
            var account = accountsTask.Result.GetValueOrDefault(person.Id);
            return new AdminUserListItem(
                person.Id,
                person.FullName,
                person.Email,
                person.DocumentNumber,
                LoginOf(account),
                rolesTask.Result.GetValueOrDefault(person.Id) ?? [],
                StatusOf(account),
                person.CreatedAt,
                account?.LastLoginAt,
                person.LinkCount);
        }).ToArray();
    }

    private async Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken cancellationToken) =>
        (await roles.GetRoleCodesByUserIdsAsync([userId], cancellationToken))
            .GetValueOrDefault(userId)?.Contains(role) ?? false;

    private static AdminResult<IssuedCredentials>? ToFailure(CreateAccountResult result) => result.Status switch
    {
        CreateAccountStatus.Created => null,
        CreateAccountStatus.Invalid => AdminResult<IssuedCredentials>.Invalid("Revisa los datos ingresados.", result.Errors),
        CreateAccountStatus.DuplicateLogin => AdminResult<IssuedCredentials>.Conflict(
            "Ya existe una cuenta con ese usuario o correo."),
        CreateAccountStatus.DuplicateDocument => AdminResult<IssuedCredentials>.Conflict(
            "Ya hay una persona registrada con ese número de documento."),
        _ => throw new InvalidOperationException($"Account creation failed: {result.Status}.")
    };

    // Students sign in with a username; guardians and staff with email.
    private static string? LoginOf(AccountSummary? account) =>
        account?.Logins.FirstOrDefault(login => login.LoginType == LoginType.Username)?.LoginIdentifier ??
        account?.Logins.FirstOrDefault()?.LoginIdentifier;

    private static string StatusOf(AccountSummary? account) => account?.Status switch
    {
        null => AdminAccountStatus.NoAccount,
        AccountStatus.Active => AdminAccountStatus.Active,
        _ => AdminAccountStatus.Inactive
    };

    private static bool TryParseStatus(string? value, out AccountStatus? status)
    {
        status = value switch
        {
            AdminAccountStatus.Active => AccountStatus.Active,
            AdminAccountStatus.Inactive => AccountStatus.Inactive,
            _ => null
        };
        return value is null || status is not null;
    }

    private static IReadOnlyCollection<Guid>? Intersect(IReadOnlyList<Guid>? first, IReadOnlyList<Guid>? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        var set = second.ToHashSet();
        return first.Where(set.Contains).ToArray();
    }

    private static async Task<IReadOnlyList<Guid>?> NullableAsync(Task<IReadOnlyList<Guid>> task) => await task;
}
