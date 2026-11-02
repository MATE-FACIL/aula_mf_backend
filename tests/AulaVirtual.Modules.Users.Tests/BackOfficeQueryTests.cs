using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Domain.Guardianship;
using AulaVirtual.Modules.Users.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Users.Tests;

public sealed class BackOfficeQueryTests
{
    private readonly SqlCapture capture = new();

    private UsersDbContext Context() => new(new DbContextOptionsBuilder<UsersDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model")
        .AddInterceptors(capture)
        .Options);

    [Fact]
    public async Task Search_FiltersEveryTermInSql_IgnoringAccents()
    {
        await using var context = Context();
        var directory = new UserDirectory(context);

        var sql = await SqlCapture.RunAsync(capture, () => directory.SearchAsync(
            new UserSearchQuery("ana 100%_", [Guid.NewGuid()], 1, 20), default));

        // The first statement is the total count: filtered in the database, never in memory.
        Assert.Contains("ILIKE", sql);
        Assert.Contains("unaccent(", sql);
        Assert.Contains("ESCAPE", sql);
        Assert.Contains("= ANY (", sql);
    }

    [Fact]
    public async Task Get_ProjectsLinkCountAsCorrelatedSubquery()
    {
        await using var context = Context();
        var directory = new UserDirectory(context);

        var sql = await SqlCapture.RunAsync(capture, () => directory.GetAsync(Guid.NewGuid(), default));

        Assert.Contains("guardian_links", sql);
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyRestriction_ReturnsWithoutQuerying()
    {
        await using var context = Context();
        var directory = new UserDirectory(context);

        var page = await directory.SearchAsync(new UserSearchQuery(null, [], 1, 20), default);

        Assert.Empty(page.Items);
        Assert.Null(capture.Sql);
    }

    [Fact]
    public async Task Unlink_DeletesInOneStatement()
    {
        await using var context = Context();
        var links = new GuardianLinkStore(context, TimeProvider.System);

        var sql = await SqlCapture.RunAsync(capture, () => links.UnlinkAsync(Guid.NewGuid(), Guid.NewGuid(), default));

        Assert.StartsWith("DELETE FROM users.guardian_links", sql.Trim());
    }

    [Fact]
    public void GuardianLink_RejectsSelfLinks()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => GuardianLink.Create(id, id, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => GuardianLink.Create(Guid.Empty, id, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GuardianLinkModel_UsesCompositeKeyAndStudentIndex()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(GuardianLink))!;

        Assert.Equal("guardian_links", entity.GetTableName());
        Assert.Equal(2, entity.FindPrimaryKey()!.Properties.Count);
        Assert.Contains(entity.GetIndexes(), index => index.GetDatabaseName() == "ix_guardian_links_student_user_id");
    }
}
