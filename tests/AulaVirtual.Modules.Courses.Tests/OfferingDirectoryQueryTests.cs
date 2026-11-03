using AulaVirtual.Modules.Courses.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Tests;

// Each query must translate to SQL: a Where/OrderBy over the projected record would fail only at runtime.
public sealed class OfferingDirectoryQueryTests
{
    private readonly SqlCapture capture = new();

    private OfferingDirectory Directory() => new(new CoursesDbContext(
        new DbContextOptionsBuilder<CoursesDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model")
            .AddInterceptors(capture)
            .Options));

    [Fact]
    public async Task Get_FiltersBeforeProjecting()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Directory().GetAsync(Guid.NewGuid(), default));
        Assert.Contains("JOIN", sql);
        Assert.Contains("maximum_capacity", sql);
    }

    [Fact]
    public async Task GetMany_UsesOneQuery()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Directory().GetManyAsync([Guid.NewGuid(), Guid.NewGuid()], default));
        Assert.Contains("= ANY (", sql);
    }

    [Fact]
    public async Task List_OrdersByCourseAndName()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Directory().ListAsync(default));
        Assert.Contains("ORDER BY", sql);
    }
}
