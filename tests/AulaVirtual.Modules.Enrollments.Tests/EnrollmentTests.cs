using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Api.Controllers;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Tests.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Enrollments.Tests;

public sealed class EnrollmentDomainTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static Enrollment New() =>
        Enrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Yape 123 ", Admin, DateTimeOffset.UtcNow);

    [Fact]
    public void Starts_Pending_AndRecordsWhoRegistered()
    {
        var enrollment = New();
        Assert.Equal(EnrollmentStatus.Pending, enrollment.Status);
        Assert.Equal(Admin, enrollment.CreatedByUserId);
        Assert.Equal("Yape 123", enrollment.PaymentNote);
        Assert.True(enrollment.HoldsSeat);
    }

    [Fact]
    public void Activation_RecordsWhoAndWhen_AndOnlyFromPending()
    {
        var enrollment = New();
        var at = DateTimeOffset.UtcNow;
        enrollment.Activate(Admin, null, at);

        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
        Assert.Equal(Admin, enrollment.ActivatedByUserId);
        Assert.Equal(at, enrollment.ActivatedAt);
        Assert.Equal("Yape 123", enrollment.PaymentNote);
        Assert.Throws<InvalidOperationException>(() => enrollment.Activate(Admin, null, at));
    }

    [Fact]
    public void Withdrawal_FreesTheSeat_AndCannotRepeat()
    {
        var enrollment = New();
        enrollment.Activate(Admin, null, DateTimeOffset.UtcNow);
        enrollment.Withdraw(Admin, DateTimeOffset.UtcNow);

        Assert.False(enrollment.HoldsSeat);
        Assert.Equal(Admin, enrollment.WithdrawnByUserId);
        Assert.Throws<InvalidOperationException>(() => enrollment.Withdraw(Admin, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => enrollment.Activate(Admin, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsEmptyIdsAndLongNotes()
    {
        Assert.Throws<ArgumentException>(() =>
            Enrollment.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), null, Admin, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Enrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', 301), Admin, DateTimeOffset.UtcNow));
    }
}

public sealed class EnrollmentUseCaseTests
{
    private readonly Fakes fakes = new();
    private readonly Guid admin = Guid.NewGuid();

    private EnrollmentUseCases UseCases() => new(fakes, fakes, fakes, fakes, TimeProvider.System);

    [Fact]
    public void Controller_RequiresEnrollmentsManagePermission()
    {
        var attribute = Assert.Single(typeof(EnrollmentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(Permissions.EnrollmentsManage, attribute.Policy);
    }

    [Fact]
    public async Task Enroll_CreatesPending_WithNamesForTheList()
    {
        var student = fakes.AddPerson("Ana", "Quispe", Roles.Student);
        var adminName = fakes.AddPerson("Equipo", "Aula", Roles.Administrator, admin);
        var offering = fakes.AddOffering("Matemáticas", "Grupo A", capacity: 8);

        var result = await UseCases().EnrollAsync(admin, student, offering, "Plin", default);

        Assert.Equal(EnrollmentResultStatus.Ok, result.Status);
        Assert.Equal(EnrollmentStatusCodes.Pending, result.Value!.Status);
        Assert.Equal("Ana Quispe", result.Value.StudentName);
        Assert.Equal("Matemáticas", result.Value.CourseTitle);
        Assert.Equal("Equipo Aula", result.Value.CreatedByName);
        Assert.NotEqual(Guid.Empty, adminName);
    }

    [Fact]
    public async Task Enroll_RejectsNonStudents_UnknownEditions_AndDuplicates()
    {
        var guardian = fakes.AddPerson("Rosa", "Roque", Roles.Guardian);
        var student = fakes.AddPerson("Ana", "Quispe", Roles.Student);
        var offering = fakes.AddOffering("Matemáticas", "Grupo A", capacity: 8);
        var useCases = UseCases();

        Assert.Equal(EnrollmentResultStatus.Invalid, (await useCases.EnrollAsync(admin, guardian, offering, null, default)).Status);
        Assert.Equal(EnrollmentResultStatus.NotFound, (await useCases.EnrollAsync(admin, student, Guid.NewGuid(), null, default)).Status);
        Assert.Equal(EnrollmentResultStatus.Ok, (await useCases.EnrollAsync(admin, student, offering, null, default)).Status);
        var duplicate = await useCases.EnrollAsync(admin, student, offering, null, default);
        Assert.Equal(EnrollmentResultStatus.Conflict, duplicate.Status);
        Assert.Contains("ya está matriculado", duplicate.Detail);
    }

    [Fact]
    public async Task Enroll_RejectsDraftEditions_WithoutTakingASeat()
    {
        var student = fakes.AddPerson("Ana", "Perez", Roles.Student);
        var draft = fakes.AddOffering("Matemáticas", "Grupo borrador", 8, published: false);
        var useCases = UseCases();

        var result = await useCases.EnrollAsync(admin, student, draft, null, default);

        Assert.Equal(EnrollmentResultStatus.Conflict, result.Status);
        Assert.Contains("borrador", result.Detail);
        Assert.Equal(0, (await useCases.ListOfferingSeatsAsync(default)).Single().TakenSeats);
    }

    [Fact]
    public async Task Enroll_RespectsCapacity_AndWithdrawalFreesTheSeat()
    {
        var offering = fakes.AddOffering("Lab IA", "Grupo B", capacity: 1);
        var first = fakes.AddPerson("Ana", "Quispe", Roles.Student);
        var second = fakes.AddPerson("Luis", "Rojas", Roles.Student);
        var useCases = UseCases();

        var taken = await useCases.EnrollAsync(admin, first, offering, null, default);
        var full = await useCases.EnrollAsync(admin, second, offering, null, default);
        Assert.Equal(EnrollmentResultStatus.Conflict, full.Status);
        Assert.Contains("lleno", full.Detail);
        Assert.Equal(0, (await useCases.ListOfferingSeatsAsync(default)).Single().FreeSeats);

        await useCases.WithdrawAsync(admin, taken.Value!.Id, default);
        Assert.Equal(EnrollmentResultStatus.Ok, (await useCases.EnrollAsync(admin, second, offering, null, default)).Status);
        // The withdrawn student cannot come back while the group is full again.
        Assert.Equal(EnrollmentResultStatus.Conflict, (await useCases.EnrollAsync(admin, first, offering, null, default)).Status);
    }

    [Fact]
    public async Task ActivateAndWithdraw_ValidateTheCurrentState()
    {
        var student = fakes.AddPerson("Ana", "Quispe", Roles.Student);
        var offering = fakes.AddOffering("Matemáticas", "Grupo A", capacity: 8);
        var useCases = UseCases();
        var id = (await useCases.EnrollAsync(admin, student, offering, null, default)).Value!.Id;

        var activated = await useCases.ActivateAsync(admin, id, "Yape 12/11", default);
        Assert.Equal(EnrollmentStatusCodes.Active, activated.Value!.Status);
        Assert.Equal("Yape 12/11", activated.Value.PaymentNote);
        Assert.NotNull(activated.Value.ActivatedAt);

        Assert.Equal(EnrollmentResultStatus.Invalid, (await useCases.ActivateAsync(admin, id, null, default)).Status);
        Assert.Equal(EnrollmentStatusCodes.Withdrawn, (await useCases.WithdrawAsync(admin, id, default)).Value!.Status);
        Assert.Equal(EnrollmentResultStatus.Invalid, (await useCases.WithdrawAsync(admin, id, default)).Status);
        Assert.Equal(EnrollmentResultStatus.NotFound, (await useCases.ActivateAsync(admin, Guid.NewGuid(), null, default)).Status);
    }

    [Fact]
    public async Task List_FiltersByStatus_AndRejectsUnknownStatus()
    {
        var offering = fakes.AddOffering("Matemáticas", "Grupo A", capacity: 8);
        var useCases = UseCases();
        var a = (await useCases.EnrollAsync(admin, fakes.AddPerson("Ana", "Quispe", Roles.Student), offering, null, default)).Value!;
        await useCases.EnrollAsync(admin, fakes.AddPerson("Luis", "Rojas", Roles.Student), offering, null, default);
        await useCases.ActivateAsync(admin, a.Id, null, default);

        var active = await useCases.ListAsync(new EnrollmentListQuery(offering, null, EnrollmentStatusCodes.Active), default);
        Assert.Equal(a.Id, Assert.Single(active.Value!.Items).Id);
        Assert.Equal(EnrollmentResultStatus.Invalid,
            (await useCases.ListAsync(new EnrollmentListQuery(null, null, "paid"), default)).Status);
    }

    private sealed class Fakes : IEnrollmentRepository, IOfferingDirectory, IUserDirectory, IRoleDirectory
    {
        private readonly List<Enrollment> enrollments = [];
        private readonly List<OfferingSummary> offerings = [];
        private readonly List<UserSummary> people = [];
        private readonly Dictionary<Guid, IReadOnlyList<string>> roles = [];

        public Guid AddPerson(string given, string surname, string role, Guid? id = null)
        {
            var userId = id ?? Guid.NewGuid();
            people.Add(new UserSummary(userId, given, surname, null, null, "DNI", "70000000", DateTimeOffset.UtcNow, 0));
            roles[userId] = [role];
            return userId;
        }

        public Guid AddOffering(string course, string name, int capacity, bool published = true)
        {
            var id = Guid.NewGuid();
            offerings.Add(new OfferingSummary(id, Guid.NewGuid(), course, name, capacity, published));
            return id;
        }

        public Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            Task.FromResult(enrollments.SingleOrDefault(item => item.Id == enrollmentId));

        public Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query,
            CancellationToken cancellationToken)
        {
            var matches = enrollments.Where(item =>
                    (query.CourseOfferingId is null || item.CourseOfferingId == query.CourseOfferingId) &&
                    (query.StudentUserId is null || item.StudentUserId == query.StudentUserId) &&
                    (query.Status is null || item.Status == query.Status))
                .ToList();
            return Task.FromResult<(IReadOnlyList<Enrollment>, int)>((matches, matches.Count));
        }

        public Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(enrollments.Where(item => item.HoldsSeat)
                .GroupBy(item => item.CourseOfferingId).ToDictionary(group => group.Key, group => group.Count()));

        public Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity,
            CancellationToken cancellationToken)
        {
            var live = enrollments.Where(item => item.CourseOfferingId == enrollment.CourseOfferingId && item.HoldsSeat).ToList();
            if (live.Any(item => item.StudentUserId == enrollment.StudentUserId))
                return Task.FromResult(SeatReservation.AlreadyEnrolled);
            if (live.Count >= maximumCapacity) return Task.FromResult(SeatReservation.Full);
            enrollments.Add(enrollment);
            return Task.FromResult(SeatReservation.Added);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Enrollment>> GetLiveByStudentsAsync(IReadOnlyCollection<Guid> studentUserIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(enrollments
                .Where(item => studentUserIds.Contains(item.StudentUserId) && item.HoldsSeat).ToList());

        public Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(enrollments
                .Where(item => item.StudentUserId == studentUserId && item.Status == EnrollmentStatus.Active)
                .Select(item => item.CourseOfferingId).ToList());

        Task<OfferingSummary?> IOfferingDirectory.GetAsync(Guid offeringId, CancellationToken cancellationToken) =>
            Task.FromResult(offerings.SingleOrDefault(item => item.Id == offeringId));

        Task<IReadOnlyList<OfferingSummary>> IOfferingDirectory.GetManyAsync(IReadOnlyCollection<Guid> offeringIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(offerings.Where(item => offeringIds.Contains(item.Id)).ToList());

        public Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(offerings);

        Task<UserSummary?> IUserDirectory.GetAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(people.SingleOrDefault(item => item.Id == userId));

        Task<IReadOnlyList<UserSummary>> IUserDirectory.GetManyAsync(IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSummary>>(people.Where(item => userIds.Contains(item.Id)).ToList());

        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(roles
                .Where(entry => userIds.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value));

        public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

public sealed class EnrollmentQueryTests
{
    private readonly SqlCapture capture = new();

    private EnrollmentRepository Repository() => new(new EnrollmentsDbContext(
        new DbContextOptionsBuilder<EnrollmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model")
            .AddInterceptors(capture)
            .Options));

    [Fact]
    public async Task Search_FiltersAndPagesInSql()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().SearchAsync(
            new EnrollmentQuery(Guid.NewGuid(), null, EnrollmentStatus.Active, 1, 20), default));

        Assert.Contains("course_offering_id", sql);
        Assert.Contains("status", sql);
    }

    [Fact]
    public async Task SeatCount_IgnoresWithdrawn_AndGroupsByEdition()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().CountSeatsAsync(null, default));

        Assert.Contains("'withdrawn'", sql);
        Assert.Contains("GROUP BY", sql);
    }

    // The seat reservation (advisory lock + count + insert) needs a real transaction:
    // it is covered against PostgreSQL by concurrent requests, not here.
}
