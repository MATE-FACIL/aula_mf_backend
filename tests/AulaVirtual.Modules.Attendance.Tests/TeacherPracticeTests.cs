using AulaVirtual.Modules.Attendance.Application;
using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Exams.Application.Practice;

namespace AulaVirtual.Modules.Attendance.Tests;

public sealed class TeacherPracticeTests
{
    private static readonly Guid Teacher = Guid.NewGuid();
    private static readonly Guid Mine = Guid.NewGuid();

    private sealed class FakeTeaching(params Guid[] courses) : ITeachingDirectory
    {
        public int Calls;

        public Task<IReadOnlyList<TeacherGroup>> GetGroupsAsync(Guid teacherUserId, CancellationToken cancellationToken)
        {
            Calls++;
            IReadOnlyList<TeacherGroup> groups = teacherUserId == Teacher
                ? courses.Select(course => new TeacherGroup(Guid.NewGuid(), course, "Curso", "Grupo", true, 8, 1, null, null)).ToArray()
                : [];
            return Task.FromResult(groups);
        }

        public Task<IReadOnlyList<GroupSession>?> GetGroupSessionsAsync(Guid teacherUserId, Guid offeringId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeContent : ICourseContentDirectory
    {
        public int Calls;

        public Task<CourseContentView?> GetPublishedContentAsync(Guid courseId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<CourseContentView?>(new CourseContentView(courseId, "Curso", []));
        }
    }

    // The editing use case is never reached when access is denied, so it needs no real collaborators here.
    private static TeacherPracticeUseCases Build(FakeTeaching teaching, FakeContent content) =>
        new(teaching, content, new PracticeAdminUseCases(null!, null!, TimeProvider.System));

    [Fact]
    public async Task Lessons_AreOnlyForACourseTheTeacherTeaches_WithOneAccessCheck()
    {
        var teaching = new FakeTeaching(Mine);
        var content = new FakeContent();
        var useCases = Build(teaching, content);

        Assert.NotNull(await useCases.GetLessonsAsync(Teacher, Mine, default));
        Assert.Null(await useCases.GetLessonsAsync(Teacher, Guid.NewGuid(), default));
        Assert.Null(await useCases.GetLessonsAsync(Guid.NewGuid(), Mine, default));
        Assert.Equal(1, content.Calls);
        Assert.Equal(3, teaching.Calls);
    }

    [Fact]
    public async Task Exercises_AnotherCourseOrAnotherTeacher_IsNotFoundAndNeverTouchesTheEditor()
    {
        var useCases = Build(new FakeTeaching(Mine), new FakeContent());
        var lesson = Guid.NewGuid();

        var read = await useCases.GetExercisesAsync(Teacher, Guid.NewGuid(), lesson, default);
        var write = await useCases.ReplaceExercisesAsync(Teacher, Guid.NewGuid(), lesson, [], default);
        var stranger = await useCases.ReplaceExercisesAsync(Guid.NewGuid(), Mine, lesson, [], default);

        Assert.All(new[] { read.Status, write.Status, stranger.Status }, status => Assert.Equal(PracticeResultStatus.NotFound, status));
    }
}
