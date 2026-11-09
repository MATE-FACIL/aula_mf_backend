using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Enrollments.Tests;

public sealed class FamilyTests
{
    private readonly Guid guardian = Guid.NewGuid();
    private readonly Guid otherGuardian = Guid.NewGuid();
    private readonly Guid ana = Guid.NewGuid();
    private readonly Guid luis = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private readonly Guid teacher = Guid.NewGuid();
    private readonly Guid groupA = Guid.NewGuid();
    private readonly Guid groupB = Guid.NewGuid();
    private readonly InMemory memory = new();

    private FamilyUseCases UseCases() => new(memory, memory, memory, memory, memory, TimeProvider.System);

    public FamilyTests()
    {
        foreach (var (id, given, surname) in new[] { (ana, "Ana", "Quispe"), (luis, "Luis", "Quispe"), (stranger, "Otro", "Niño"), (teacher, "Lucía", "Paredes") })
            memory.People.Add(new UserSummary(id, given, surname, null, null, null, null, DateTimeOffset.UtcNow, 0));
        memory.Links.AddRange([(guardian, ana), (guardian, luis), (otherGuardian, stranger)]);
        memory.Editions.Add(new OfferingSummary(groupA, Guid.NewGuid(), "Matemáticas", "Grupo A", 8, true));
        memory.Editions.Add(new OfferingSummary(groupB, Guid.NewGuid(), "Lab IA", "Grupo B", 12, true));

        var active = Enrollment.Create(Guid.NewGuid(), ana, groupA, null, teacher, DateTimeOffset.UtcNow);
        active.Activate(teacher, null, DateTimeOffset.UtcNow);
        memory.Seats.Add(active);
        memory.Seats.Add(Enrollment.Create(Guid.NewGuid(), luis, groupB, null, teacher, DateTimeOffset.UtcNow));
        memory.Seats.Add(Enrollment.Create(Guid.NewGuid(), stranger, groupA, null, teacher, DateTimeOffset.UtcNow));

        var start = DateTimeOffset.UtcNow.AddDays(1);
        memory.Sessions.Add(new ScheduledSession(Guid.NewGuid(), groupA, "Grupo A", "Matemáticas", null, start, start.AddHours(1), null, [teacher]));
        memory.Sessions.Add(new ScheduledSession(Guid.NewGuid(), groupB, "Grupo B", "Lab IA", null, start, start.AddHours(1), null, []));
    }

    [Fact]
    public async Task Guardian_SeesOnlyTheirOwnChildren()
    {
        var children = await UseCases().ListChildrenAsync(guardian, default);

        Assert.Equal(["Ana Quispe", "Luis Quispe"], children.Select(child => child.FullName));
        Assert.DoesNotContain(children, child => child.StudentUserId == stranger);
    }

    [Fact]
    public async Task EachChild_HasTheirEnrollments_AndClassesOnlyFromActiveOnes()
    {
        var children = await UseCases().ListChildrenAsync(guardian, default);
        var withActive = children.Single(child => child.StudentUserId == ana);
        var withPending = children.Single(child => child.StudentUserId == luis);

        Assert.Equal(EnrollmentStatusCodes.Active, Assert.Single(withActive.Enrollments).Status);
        Assert.Equal(["Lucía Paredes"], Assert.Single(withActive.UpcomingClasses).Teachers);

        Assert.Equal(EnrollmentStatusCodes.Pending, Assert.Single(withPending.Enrollments).Status);
        Assert.Empty(withPending.UpcomingClasses);
    }

    [Fact]
    public async Task Upcoming_IsLimitedPerChild()
    {
        var start = DateTimeOffset.UtcNow.AddDays(2);
        for (var i = 0; i < 10; i++)
            memory.Sessions.Add(new ScheduledSession(Guid.NewGuid(), groupA, "Grupo A", "Matemáticas", null, start.AddHours(i), start.AddHours(i + 1), null, []));

        var child = (await UseCases().ListChildrenAsync(guardian, default)).Single(item => item.StudentUserId == ana);

        Assert.Equal(FamilyUseCases.UpcomingClassesPerChild, child.UpcomingClasses.Count);
    }

    [Fact]
    public async Task AccountWithoutChildren_GetsAnEmptyList_WithoutFurtherQueries()
    {
        Assert.Empty(await UseCases().ListChildrenAsync(Guid.NewGuid(), default));
        Assert.Equal(0, memory.PeopleQueries);
    }

    [Fact]
    public async Task NoQueryRunsPerChild()
    {
        await UseCases().ListChildrenAsync(guardian, default);
        Assert.Equal(1, memory.PeopleQueries);
    }

    [Fact]
    public async Task LiveSeats_TranslateToSql()
    {
        var capture = new SqlCapture();
        var repository = new EnrollmentRepository(new EnrollmentsDbContext(new DbContextOptionsBuilder<EnrollmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options));

        var sql = await SqlCapture.RunAsync(capture, () => repository.GetLiveByStudentsAsync([Guid.NewGuid(), Guid.NewGuid()], default));

        Assert.Contains("= ANY (", sql);
        Assert.Contains("'withdrawn'", sql);
    }

    private sealed class InMemory : IGuardianLinks, IEnrollmentRepository, IOfferingDirectory, IOfferingScheduleDirectory, IUserDirectory
    {
        public List<(Guid Guardian, Guid Student)> Links { get; } = [];
        public List<Enrollment> Seats { get; } = [];
        public List<OfferingSummary> Editions { get; } = [];
        public List<ScheduledSession> Sessions { get; } = [];
        public List<UserSummary> People { get; } = [];
        public int PeopleQueries { get; private set; }

        public Task<IReadOnlyList<Guid>> GetStudentIdsAsync(Guid guardianUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Links.Where(link => link.Guardian == guardianUserId).Select(link => link.Student).ToList());

        public Task<IReadOnlyList<Enrollment>> GetLiveByStudentsAsync(IReadOnlyCollection<Guid> studentUserIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(Seats.Where(seat => studentUserIds.Contains(seat.StudentUserId) && seat.HoldsSeat).ToList());

        Task<IReadOnlyList<OfferingSummary>> IOfferingDirectory.GetManyAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(Editions.Where(edition => offeringIds.Contains(edition.Id)).ToList());

        public Task<SessionDetails?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ScheduledSession>> GetSessionsAsync(IReadOnlyCollection<Guid> offeringIds, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduledSession>>(Sessions
                .Where(session => offeringIds.Contains(session.OfferingId) && session.StartsAt < to && session.EndsAt > from)
                .OrderBy(session => session.StartsAt).Take(limit).ToList());

        Task<IReadOnlyList<UserSummary>> IUserDirectory.GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
        {
            PeopleQueries++;
            return Task.FromResult<IReadOnlyList<UserSummary>>(People.Where(person => userIds.Contains(person.Id)).ToList());
        }

        public Task<bool> LinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UnlinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetGuardianIdsAsync(Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<OfferingSummary?> IOfferingDirectory.GetAsync(Guid offeringId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<UserSummary?> IUserDirectory.GetAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
