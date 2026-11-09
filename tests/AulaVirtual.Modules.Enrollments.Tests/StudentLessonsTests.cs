using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Domain;

namespace AulaVirtual.Modules.Enrollments.Tests;

public sealed class StudentLessonsTests
{
    private readonly Guid student = Guid.NewGuid();
    private readonly Guid math = Guid.NewGuid();
    private readonly Guid lab = Guid.NewGuid();
    private readonly Fakes fakes = new();

    private StudentLessonsUseCases UseCases() => new(fakes, fakes, fakes, fakes);

    [Fact]
    public async Task ActiveCourse_ShowsItsPublishedLessons()
    {
        var group = fakes.AddOffering(math, "Matemáticas", published: true);
        fakes.Active[student] = [group];

        var courses = await UseCases().ListCoursesAsync(student, default);
        var content = await UseCases().GetLessonsAsync(student, math, default);

        Assert.Equal(math, Assert.Single(courses).CourseId);
        Assert.Equal("intermediate", courses[0].Level?.Level);
        Assert.Equal(math, content!.CourseId);
        Assert.Equal([math], fakes.ContentRequests);
    }

    [Fact]
    public async Task CourseWithoutActiveEnrollment_IsNotShown_AndContentIsNeverRead()
    {
        var group = fakes.AddOffering(math, "Matemáticas", published: true);
        fakes.Active[student] = [group];

        Assert.Null(await UseCases().GetLessonsAsync(student, lab, default));
        Assert.Null(await UseCases().GetLessonsAsync(Guid.NewGuid(), math, default));
        Assert.Empty(fakes.ContentRequests);
    }

    [Fact]
    public async Task DraftGroups_AreLeftOut()
    {
        var draft = fakes.AddOffering(lab, "Laboratorio", published: false);
        fakes.Active[student] = [draft];

        Assert.Empty(await UseCases().ListCoursesAsync(student, default));
        Assert.Null(await UseCases().GetLessonsAsync(student, lab, default));
    }

    private sealed class Fakes : IEnrollmentRepository, IOfferingDirectory, ICourseContentDirectory, IMathLevelDirectory
    {
        public Task<IReadOnlyList<LevelOfCourse>> GetLevelsAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelOfCourse>>(courseIds.Select(id => new LevelOfCourse(id, "secondary", "intermediate", "Secundaria", "Intermedio")).ToList());

        public Dictionary<Guid, IReadOnlyList<Guid>> Active { get; } = [];
        public List<Guid> ContentRequests { get; } = [];
        private readonly List<OfferingSummary> offerings = [];

        public Guid AddOffering(Guid courseId, string title, bool published)
        {
            var id = Guid.NewGuid();
            offerings.Add(new OfferingSummary(id, courseId, title, "Grupo A", 8, published));
            return id;
        }

        public Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Active.GetValueOrDefault(studentUserId) ?? []);

        public Task<IReadOnlyList<OfferingSummary>> GetManyAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(offerings.Where(o => offeringIds.Contains(o.Id)).ToList());

        public Task<CourseContentView?> GetPublishedContentAsync(Guid courseId, CancellationToken cancellationToken)
        {
            ContentRequests.Add(courseId);
            return Task.FromResult<CourseContentView?>(new CourseContentView(courseId, "Curso", []));
        }

        Task<OfferingSummary?> IOfferingDirectory.GetAsync(Guid offeringId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Enrollment>> GetLiveByStudentsAsync(IReadOnlyCollection<Guid> studentUserIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
