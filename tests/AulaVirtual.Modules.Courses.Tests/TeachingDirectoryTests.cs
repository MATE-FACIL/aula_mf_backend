using AulaVirtual.Modules.Courses.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Tests;

// The teacher panel and practice read contracts translate to PostgreSQL and start from indexed columns.
public sealed class TeachingDirectoryTests
{
    private readonly SqlCapture capture = new();

    private TeachingDirectory Directory() => new(new CoursesDbContext(new DbContextOptionsBuilder<CoursesDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options), TimeProvider.System);

    [Fact]
    public async Task TeacherGroups_StartFromTheTeachersUserId_InOneJoin()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Directory().GetGroupsAsync(Guid.NewGuid(), default));
        Assert.Contains("user_id", sql);
        Assert.Contains("course_offering_instructors", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public async Task GroupSessions_CheckTheAssignmentFirst()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Directory().GetGroupSessionsAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Contains("EXISTS", sql);
    }

    [Fact]
    public async Task PublishedLessons_AreInCourseOrder()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Directory().GetPublishedLessonsAsync(Guid.NewGuid(), default));
        Assert.Contains("is_published", sql);
        Assert.Contains("course_units", sql);
        Assert.Contains("ORDER BY", sql);
    }
}
