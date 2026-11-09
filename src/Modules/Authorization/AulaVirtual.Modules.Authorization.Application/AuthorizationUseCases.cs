using AulaVirtual.Modules.Authorization.Domain;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Authorization.Application;

public sealed record EffectiveAuthorization(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed class AuthorizationUseCases(IAuthorizationRepository repository, IUserExistence users,
    IPermissionChecker checker)
{
    public async Task<EffectiveAuthorization> GetEffectiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        Role.RequireId(userId);
        return new EffectiveAuthorization(await repository.GetRoleCodesAsync(userId, cancellationToken),
            await repository.GetPermissionCodesAsync(userId, cancellationToken));
    }

    public Task<bool> HasPermissionAsync(Guid userId, string code, CancellationToken cancellationToken) =>
        checker.HasPermissionAsync(userId, code, cancellationToken);

    public async Task<bool> AssignRoleAsync(Guid userId, string roleCode, CancellationToken cancellationToken)
    {
        Role.RequireId(userId);
        roleCode = Role.RequireCode(roleCode);
        if (!await users.ExistsAsync(userId, cancellationToken))
            throw new ArgumentException("User does not exist.", nameof(userId));
        var role = await repository.GetRoleByCodeAsync(roleCode, cancellationToken)
            ?? throw new ArgumentException("Role does not exist.", nameof(roleCode));
        if (await repository.UserHasRoleAsync(userId, role.Id, cancellationToken)) return false;
        repository.Add(UserRole.Create(userId, role.Id));
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Explicit controlled bootstrap operation. The caller supplies a verified UserId.
    public async Task<bool> AssignFirstAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        Role.RequireId(userId);
        if (!await users.ExistsAsync(userId, cancellationToken))
            throw new ArgumentException("User does not exist.", nameof(userId));
        var role = await repository.GetRoleByCodeAsync(Roles.Administrator, cancellationToken)
            ?? throw new InvalidOperationException("Authorization catalog must be provisioned first.");
        return await repository.TryAssignFirstAdministratorAsync(userId, role.Id, cancellationToken);
    }

    public async Task<bool> RevokeRoleAsync(Guid userId, string roleCode, CancellationToken cancellationToken)
    {
        Role.RequireId(userId);
        var role = await repository.GetRoleByCodeAsync(Role.RequireCode(roleCode), cancellationToken);
        if (role is null || !await repository.UserHasRoleAsync(userId, role.Id, cancellationToken)) return false;
        await repository.RemoveUserRoleAsync(userId, role.Id, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeRolePermissionAsync(string roleCode, string permissionCode,
        CancellationToken cancellationToken)
    {
        var role = await repository.GetRoleByCodeAsync(Role.RequireCode(roleCode), cancellationToken);
        var permission = await repository.GetPermissionByCodeAsync(Role.RequireCode(permissionCode), cancellationToken);
        if (role is null || permission is null ||
            !await repository.RoleHasPermissionAsync(role.Id, permission.Id, cancellationToken)) return false;
        await repository.RemoveRolePermissionAsync(role.Id, permission.Id, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class CatalogProvisioner(IAuthorizationRepository repository)
{
    // v2: guardian role and users.manage permission. v3: enrollments.manage. v4: attendance.manage.
    public const int CurrentVersion = 4;
    public const string CatalogCode = "aulavirtual";

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        var existingVersion = await repository.GetCatalogVersionAsync(CatalogCode, cancellationToken);
        if (existingVersion is { Version: > CurrentVersion })
            throw new InvalidOperationException("Catalog version is newer than this application.");

        var administrator = await EnsureRoleAsync(Roles.Administrator, "Administrator", cancellationToken);
        await EnsureRoleAsync(Roles.Student, "Student", cancellationToken);
        await EnsureRoleAsync(Roles.Instructor, "Instructor", cancellationToken);
        await EnsureRoleAsync(Roles.Guardian, "Guardian", cancellationToken);
        foreach (var (code, name) in new[]
        {
            (Permissions.CoursesManage, "Manage courses"),
            (Permissions.CourseOfferingsManage, "Manage course offerings"),
            (Permissions.InstructorsManage, "Manage instructors"),
            (Permissions.UsersManage, "Manage users and families"),
            (Permissions.EnrollmentsManage, "Manage enrollments"),
            (Permissions.AttendanceManage, "Take attendance in any class")
        })
        {
            var permission = await EnsurePermissionAsync(code, name, cancellationToken);
            if (!await repository.RoleHasPermissionAsync(administrator.Id, permission.Id, cancellationToken))
                repository.Add(RolePermission.Create(administrator.Id, permission.Id));
        }

        if (existingVersion is null) repository.Add(CatalogVersion.Create(CatalogCode, CurrentVersion));
        else existingVersion.AdvanceTo(CurrentVersion);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> EnsureRoleAsync(string code, string name, CancellationToken token)
    {
        var role = await repository.GetRoleByCodeAsync(code, token);
        if (role is not null) return role;
        role = Role.Create(Guid.NewGuid(), code, name);
        repository.Add(role);
        return role;
    }

    private async Task<Permission> EnsurePermissionAsync(string code, string name, CancellationToken token)
    {
        var permission = await repository.GetPermissionByCodeAsync(code, token);
        if (permission is not null) return permission;
        permission = Permission.Create(Guid.NewGuid(), code, name);
        repository.Add(permission);
        return permission;
    }
}
