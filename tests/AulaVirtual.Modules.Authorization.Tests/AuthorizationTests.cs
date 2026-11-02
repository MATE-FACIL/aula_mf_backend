using System.Security.Claims;
using AulaVirtual.Modules.Authorization.Api;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Authorization.Domain;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Courses.Domain.Instructors;
using AulaVirtual.Modules.Users.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AulaVirtual.Modules.Authorization.Tests;

public sealed class AuthorizationTests
{
    private readonly FakeRepository repository = new();
    private readonly FakeUsers users = new();
    private AuthorizationUseCases UseCases() => new(repository, users, new FakeChecker(repository));

    [Fact]
    public async Task ProvisioningIsIdempotentAndAssignsOnlyAdministratorCapabilities()
    {
        var provisioner = new CatalogProvisioner(repository);
        await provisioner.ProvisionAsync(default);
        await provisioner.ProvisionAsync(default);
        Assert.Equal(Roles.All.Count, repository.Roles.Count);
        Assert.Equal(Permissions.All.Count, repository.Permissions.Count);
        Assert.Equal(Permissions.All.Count, repository.RolePermissions.Count);
        Assert.Single(repository.Versions);
        var service = UseCases();
        var admin = Guid.NewGuid();
        var student = Guid.NewGuid();
        var instructor = Guid.NewGuid();
        var guardian = Guid.NewGuid();
        users.Existing.UnionWith([admin, student, instructor, guardian]);
        Assert.True(await service.AssignFirstAdministratorAsync(admin, default));
        Assert.False(await service.AssignFirstAdministratorAsync(admin, default));
        Assert.Single(repository.UserRoles, x => x.UserId == admin);
        Assert.False(await service.AssignFirstAdministratorAsync(student, default));
        await service.AssignRoleAsync(student, Roles.Student, default);
        await service.AssignRoleAsync(instructor, Roles.Instructor, default);
        await service.AssignRoleAsync(guardian, Roles.Guardian, default);
        Assert.Equal(Permissions.All.OrderBy(x => x), (await service.GetEffectiveAsync(admin, default)).Permissions);
        Assert.Empty((await service.GetEffectiveAsync(student, default)).Permissions);
        Assert.Empty((await service.GetEffectiveAsync(instructor, default)).Permissions);
        Assert.Empty((await service.GetEffectiveAsync(guardian, default)).Permissions);
    }

    [Fact]
    public async Task MultipleRolesAggregateAndRevocationsAffectNextEvaluation()
    {
        await new CatalogProvisioner(repository).ProvisionAsync(default);
        var userId = Guid.NewGuid();
        users.Existing.Add(userId);
        var service = UseCases();
        await service.AssignRoleAsync(userId, Roles.Student, default);
        Assert.False(await service.AssignRoleAsync(userId, Roles.Student, default));
        await service.AssignRoleAsync(userId, Roles.Administrator, default);
        Assert.Equal(2, (await service.GetEffectiveAsync(userId, default)).Roles.Count);
        Assert.True(await service.HasPermissionAsync(userId, Permissions.CoursesManage, default));
        await service.RevokeRolePermissionAsync(Roles.Administrator, Permissions.CoursesManage, default);
        Assert.False(await service.HasPermissionAsync(userId, Permissions.CoursesManage, default));
        Assert.True(await service.HasPermissionAsync(userId, Permissions.InstructorsManage, default));
        await service.RevokeRoleAsync(userId, Roles.Administrator, default);
        Assert.False(await service.HasPermissionAsync(userId, Permissions.InstructorsManage, default));
    }

    [Fact]
    public async Task ConcurrentBootstrapAttemptsOnlyAssignOneUserAndLeaveNormalRoleAssignmentAvailable()
    {
        await new CatalogProvisioner(repository).ProvisionAsync(default);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        users.Existing.UnionWith([first, second]);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt(Guid userId)
        {
            await start.Task;
            return await UseCases().AssignFirstAdministratorAsync(userId, default);
        }
        var attempts = new[] { Task.Run(() => Attempt(first)), Task.Run(() => Attempt(second)) };
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);
        Assert.Single(outcomes, outcome => outcome);
        var roleId = repository.Roles.Single(role => role.Code == Roles.Administrator).Id;
        Assert.Single(repository.UserRoles, assignment => assignment.RoleId == roleId);
        var other = repository.UserRoles.Single().UserId == first ? second : first;
        Assert.True(await UseCases().AssignRoleAsync(other, Roles.Administrator, default));
        Assert.Equal(2, repository.UserRoles.Count(assignment => assignment.RoleId == roleId));
    }

    [Fact]
    public async Task AssignmentsRequireExplicitExistingUserAndNeverInferAcademicInstructorOrLoginType()
    {
        await new CatalogProvisioner(repository).ProvisionAsync(default);
        var userId = Guid.NewGuid();
        var service = UseCases();
        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignRoleAsync(userId, Roles.Administrator, default));
        users.Existing.Add(userId);
        var account = AuthAccount.Create(Guid.NewGuid(), userId, "encoded", DateTimeOffset.UtcNow);
        account.AddIdentifier(Guid.NewGuid(), LoginType.Email, "instructor@example.test", DateTimeOffset.UtcNow);
        var academicInstructor = Instructor.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        academicInstructor.LinkUser(userId, DateTimeOffset.UtcNow);
        Assert.Equal(userId, account.UserId);
        Assert.Equal(userId, academicInstructor.UserId);
        Assert.Empty((await service.GetEffectiveAsync(userId, default)).Roles);
        Assert.False(await service.HasPermissionAsync(userId, Permissions.CoursesManage, default));
    }

    [Fact]
    public async Task PolicyReadsUserIdAndCurrentRepositoryOnEveryEvaluation()
    {
        await new CatalogProvisioner(repository).ProvisionAsync(default);
        var userId = Guid.NewGuid();
        users.Existing.Add(userId);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IPermissionChecker>(new FakeChecker(repository));
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("user_id", userId.ToString()), new Claim("sub", Guid.NewGuid().ToString())], "test"));
        Assert.False((await authorization.AuthorizeAsync(anonymous, Permissions.CoursesManage)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(authenticated, Permissions.CoursesManage)).Succeeded);
        await UseCases().AssignRoleAsync(userId, Roles.Administrator, default);
        Assert.True((await authorization.AuthorizeAsync(authenticated, Permissions.CoursesManage)).Succeeded);
        await UseCases().RevokeRoleAsync(userId, Roles.Administrator, default);
        Assert.False((await authorization.AuthorizeAsync(authenticated, Permissions.CoursesManage)).Succeeded);
        var emailOnly = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "admin@example.test")], "test"));
        Assert.False((await authorization.AuthorizeAsync(emailOnly, Permissions.CoursesManage)).Succeeded);
    }

    [Fact]
    public void DomainAndEfModelRejectDuplicateKeysAndDoNotRelateToUsersEf()
    {
        Assert.Throws<ArgumentException>(() => Role.Create(Guid.NewGuid(), "", "Role"));
        Assert.Throws<ArgumentException>(() => Permission.Create(Guid.NewGuid(), "Upper", "Permission"));
        Assert.Throws<ArgumentException>(() => UserRole.Create(Guid.Empty, Guid.NewGuid()));
    }

    internal sealed class FakeUsers : IUserExistence
    {
        public HashSet<Guid> Existing { get; } = [];
        public Task<bool> ExistsAsync(Guid userId, CancellationToken token) => Task.FromResult(Existing.Contains(userId));
    }

    internal sealed class FakeChecker(FakeRepository repository) : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, string code, CancellationToken token) =>
            repository.HasPermissionAsync(userId, code, token);
    }

    internal sealed class FakeRepository : IAuthorizationRepository
    {
        private readonly SemaphoreSlim bootstrapGate = new(1, 1);
        public List<Role> Roles { get; } = [];
        public List<Permission> Permissions { get; } = [];
        public List<UserRole> UserRoles { get; } = [];
        public List<RolePermission> RolePermissions { get; } = [];
        public List<CatalogVersion> Versions { get; } = [];
        public Task<Role?> GetRoleByCodeAsync(string code, CancellationToken token) => Task.FromResult(Roles.SingleOrDefault(x => x.Code == code));
        public Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken token) => Task.FromResult(Permissions.SingleOrDefault(x => x.Code == code));
        public Task<CatalogVersion?> GetCatalogVersionAsync(string code, CancellationToken token) => Task.FromResult(Versions.SingleOrDefault(x => x.Code == code));
        public Task<bool> UserHasRoleAsync(Guid userId, Guid roleId, CancellationToken token) => Task.FromResult(UserRoles.Any(x => x.UserId == userId && x.RoleId == roleId));
        public async Task<bool> TryAssignFirstAdministratorAsync(Guid userId, Guid roleId, CancellationToken token)
        {
            await bootstrapGate.WaitAsync(token);
            try
            {
                if (UserRoles.Any(x => x.RoleId == roleId)) return false;
                await Task.Yield();
                UserRoles.Add(UserRole.Create(userId, roleId));
                return true;
            }
            finally { bootstrapGate.Release(); }
        }
        public Task<bool> RoleHasPermissionAsync(Guid roleId, Guid permissionId, CancellationToken token) => Task.FromResult(RolePermissions.Any(x => x.RoleId == roleId && x.PermissionId == permissionId));
        public Task<IReadOnlyList<string>> GetRoleCodesAsync(Guid userId, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(
            UserRoles.Where(x => x.UserId == userId).Join(Roles, x => x.RoleId, x => x.Id, (_, r) => r.Code).Distinct().Order().ToArray());
        public Task<IReadOnlyList<string>> GetPermissionCodesAsync(Guid userId, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(
            (from ur in UserRoles where ur.UserId == userId join rp in RolePermissions on ur.RoleId equals rp.RoleId
             join p in Permissions on rp.PermissionId equals p.Id select p.Code).Distinct().Order().ToArray());
        public Task<bool> HasPermissionAsync(Guid userId, string code, CancellationToken token) => Task.FromResult(
            UserRoles.Where(x => x.UserId == userId).Any(ur => RolePermissions.Any(rp => rp.RoleId == ur.RoleId &&
                Permissions.Any(p => p.Id == rp.PermissionId && p.Code == code))));
        public void Add(Role role) { if (!Roles.Any(x => x.Code == role.Code)) Roles.Add(role); }
        public void Add(Permission permission) { if (!Permissions.Any(x => x.Code == permission.Code)) Permissions.Add(permission); }
        public void Add(UserRole userRole) { if (!UserRoles.Any(x => x.UserId == userRole.UserId && x.RoleId == userRole.RoleId)) UserRoles.Add(userRole); }
        public void Add(RolePermission rolePermission) { if (!RolePermissions.Any(x => x.RoleId == rolePermission.RoleId && x.PermissionId == rolePermission.PermissionId)) RolePermissions.Add(rolePermission); }
        public void Add(CatalogVersion version) => Versions.Add(version);
        public Task RemoveUserRoleAsync(Guid userId, Guid roleId, CancellationToken token)
        { UserRoles.RemoveAll(x => x.UserId == userId && x.RoleId == roleId); return Task.CompletedTask; }
        public Task RemoveRolePermissionAsync(Guid roleId, Guid permissionId, CancellationToken token)
        { RolePermissions.RemoveAll(x => x.RoleId == roleId && x.PermissionId == permissionId); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken token) => Task.CompletedTask;
    }
}
