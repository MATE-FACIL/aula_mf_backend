using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Enrollments.Tests;

public sealed class StudentScheduleTests
{
    private readonly Guid student = Guid.NewGuid();
    private readonly Guid teacher = Guid.NewGuid();
    private readonly Guid activeGroup = Guid.NewGuid();
    private readonly InMemory memory = new();

    private StudentScheduleUseCases UseCases() => new(memory, memory, memory, TimeProvider.System);

    [Fact]
    public async Task OnlyActiveEnrollments_ShowClasses_WithTeacherNames()
    {
        memory.ActiveOfferings[student] = [activeGroup];
        memory.People.Add(new UserSummary(teacher, "Lucía", "Paredes", null, null, null, null, DateTimeOffset.UtcNow, 0));
        memory.Sessions.Add(new ScheduledSession(Guid.NewGuid(), activeGroup, "Grupo A", "Matemáticas", "Matemáticas",
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(90), "https://meet.example.com/a", [teacher]));

        var classes = await UseCases().ListClassesAsync(student, null, null, default);

        var item = Assert.Single(classes!);
        Assert.Equal(["Lucía Paredes"], item.Teachers);
        Assert.Equal("https://meet.example.com/a", item.VirtualLink);
        Assert.Equal([activeGroup], memory.RequestedOfferings);
    }

    [Fact]
    public async Task WithoutActiveEnrollments_DoesNotQueryCourses()
    {
        var classes = await UseCases().ListClassesAsync(student, null, null, default);

        Assert.Empty(classes!);
        Assert.Null(memory.RequestedOfferings);
    }

    [Fact]
    public async Task RejectsInvertedOrTooLongRanges()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(await UseCases().ListClassesAsync(student, now, now.AddDays(-1), default));
        Assert.Null(await UseCases().ListClassesAsync(student, now, now.AddDays(200), default));
    }

    [Fact]
    public async Task ActiveOfferings_AndSessions_TranslateToSql()
    {
        var capture = new SqlCapture();
        var enrollments = new EnrollmentRepository(new EnrollmentsDbContext(new DbContextOptionsBuilder<EnrollmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options));

        var sql = await SqlCapture.RunAsync(capture, () => enrollments.GetActiveOfferingIdsAsync(student, default));

        Assert.Contains("student_user_id", sql);
        Assert.Contains("status", sql);
    }

    private sealed class InMemory : IEnrollmentRepository, IOfferingScheduleDirectory, IUserDirectory
    {
        public Dictionary<Guid, IReadOnlyList<Guid>> ActiveOfferings { get; } = [];
        public List<ScheduledSession> Sessions { get; } = [];
        public List<UserSummary> People { get; } = [];
        public IReadOnlyCollection<Guid>? RequestedOfferings { get; private set; }

        public Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult(ActiveOfferings.GetValueOrDefault(studentUserId) ?? []);

        public Task<SessionDetails?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ScheduledSession>> GetSessionsAsync(IReadOnlyCollection<Guid> offeringIds,
            DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken)
        {
            RequestedOfferings = offeringIds;
            return Task.FromResult<IReadOnlyList<ScheduledSession>>(Sessions
                .Where(s => offeringIds.Contains(s.OfferingId) && s.StartsAt < to && s.EndsAt > from).Take(limit).ToList());
        }

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSummary>>(People.Where(p => userIds.Contains(p.Id)).ToList());

        public Task<IReadOnlyList<Enrollment>> GetLiveByStudentsAsync(IReadOnlyCollection<Guid> studentUserIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<UserSummary?> IUserDirectory.GetAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
