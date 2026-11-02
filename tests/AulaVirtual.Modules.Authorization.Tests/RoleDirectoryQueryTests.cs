using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Authorization.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Authorization.Tests;

public sealed class RoleDirectoryQueryTests
{
    private readonly SqlCapture capture = new();

    private RoleDirectory Directory() => new(new AuthorizationDbContext(
        new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model")
            .AddInterceptors(capture)
            .Options));

    [Fact]
    public async Task RolesForAPage_AreResolvedInOneQuery()
    {
        var sql = await SqlCapture.RunAsync(capture, () =>
            Directory().GetRoleCodesByUserIdsAsync([Guid.NewGuid(), Guid.NewGuid()], default));

        Assert.Contains("= ANY (", sql);
        Assert.Contains("JOIN", sql);
    }

    [Fact]
    public async Task UsersInRole_AndCounts_TranslateToSql()
    {
        var inRole = await SqlCapture.RunAsync(capture, () =>
            Directory().GetUserIdsInRoleAsync(Roles.Guardian, default));
        var counts = await SqlCapture.RunAsync(capture, () => Directory().CountUsersByRoleAsync(default));

        Assert.Contains("DISTINCT", inRole);
        Assert.Contains("GROUP BY", counts);
    }

    [Fact]
    public async Task NoUsers_ReturnsWithoutQuerying()
    {
        Assert.Empty(await Directory().GetRoleCodesByUserIdsAsync([], default));
        Assert.Null(capture.Sql);
    }
}
