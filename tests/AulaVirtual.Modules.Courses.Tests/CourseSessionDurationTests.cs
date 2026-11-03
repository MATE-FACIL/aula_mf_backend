using AulaVirtual.Modules.Courses.Application.Courses;
using AulaVirtual.Modules.Courses.Domain.Courses;

namespace AulaVirtual.Modules.Courses.Tests;

// SCRUM-42: class length configurable per course (primary 60 min, secondary 90 min).
public sealed class CourseSessionDurationTests
{
    [Fact]
    public void Duration_IsOptional_AndBounded()
    {
        var course = Course.Create(Guid.NewGuid(), "Matemáticas", null, DateTimeOffset.UtcNow);
        Assert.Null(course.SessionDurationMinutes);

        course.SetSessionDuration(90, DateTimeOffset.UtcNow);
        Assert.Equal(90, course.SessionDurationMinutes);

        course.SetSessionDuration(null, DateTimeOffset.UtcNow);
        Assert.Null(course.SessionDurationMinutes);

        Assert.Throws<ArgumentOutOfRangeException>(() => course.SetSessionDuration(29, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => course.SetSessionDuration(241, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(60, true)]
    [InlineData(10, false)]
    [InlineData(300, false)]
    public void Validator_ChecksTheRange(int? minutes, bool valid)
    {
        var result = new SaveCourseValidator().Validate(
            new SaveCourseCommand(null, "Matemáticas", null, null, null, null, null, null, minutes));
        Assert.Equal(valid, result.IsValid);
    }
}
