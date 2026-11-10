using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Enrollments.Tests;

public sealed class EnrollmentTransferDomainTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static Enrollment Pending() =>
        Enrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Yape 12/11", Admin, DateTimeOffset.UtcNow);

    [Fact]
    public void MovingAPendingSeat_WithdrawsTheOldOne_AndStartsAPendingOneInTheDestination()
    {
        var old = Pending();
        var destination = Guid.NewGuid();

        var moved = old.TransferTo(Guid.NewGuid(), destination, Other, DateTimeOffset.UtcNow);

        Assert.Equal(EnrollmentStatus.Withdrawn, old.Status);
        Assert.Equal(Other, old.WithdrawnByUserId);
        Assert.Equal((EnrollmentStatus.Pending, destination, old.StudentUserId, "Yape 12/11", Other),
            (moved.Status, moved.CourseOfferingId, moved.StudentUserId, moved.PaymentNote, moved.CreatedByUserId));
        Assert.True(moved.HoldsSeat);
    }

    [Fact]
    public void AConfirmedPayment_StaysConfirmedWithWhoConfirmedItAndWhen()
    {
        var old = Pending();
        var confirmedAt = DateTimeOffset.UtcNow.AddDays(-2);
        old.Activate(Admin, null, confirmedAt);

        var moved = old.TransferTo(Guid.NewGuid(), Guid.NewGuid(), Other, DateTimeOffset.UtcNow);

        Assert.Equal((EnrollmentStatus.Active, Admin, confirmedAt), (moved.Status, moved.ActivatedByUserId, moved.ActivatedAt));
    }

    [Fact]
    public void AWithdrawnSeatCannotBeMoved_NorToTheSameGroup()
    {
        var old = Pending();
        Assert.Throws<ArgumentException>(() => old.TransferTo(Guid.NewGuid(), old.CourseOfferingId, Other, DateTimeOffset.UtcNow));
        old.Withdraw(Admin, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => old.TransferTo(Guid.NewGuid(), Guid.NewGuid(), Other, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TheRecord_TiesBothSeats_AndTrimsTheReason()
    {
        var old = Pending();
        var moved = old.TransferTo(Guid.NewGuid(), Guid.NewGuid(), Other, DateTimeOffset.UtcNow);

        var record = EnrollmentTransfer.Record(Guid.NewGuid(), old, moved, "  El diagnóstico confirmó Intermedio  ", Other, DateTimeOffset.UtcNow);

        Assert.Equal((old.Id, moved.Id, old.CourseOfferingId, moved.CourseOfferingId, "El diagnóstico confirmó Intermedio"),
            (record.FromEnrollmentId, record.ToEnrollmentId, record.FromOfferingId, record.ToOfferingId, record.Reason));
        Assert.Null(EnrollmentTransfer.Record(Guid.NewGuid(), old, moved, "   ", Other, DateTimeOffset.UtcNow).Reason);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnrollmentTransfer.Record(Guid.NewGuid(), old, moved, new string('x', EnrollmentTransfer.MaxReasonLength + 1), Other, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TheRecord_NeedsTheSameStudentAndTwoDifferentGroups()
    {
        var a = Pending();
        var b = Pending();
        Assert.Throws<ArgumentException>(() => EnrollmentTransfer.Record(Guid.NewGuid(), a, b, null, Other, DateTimeOffset.UtcNow));
        var sameGroup = Enrollment.Create(Guid.NewGuid(), a.StudentUserId, a.CourseOfferingId, null, Admin, DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => EnrollmentTransfer.Record(Guid.NewGuid(), a, sameGroup, null, Other, DateTimeOffset.UtcNow));
    }
}

public sealed class TransferAndPlacementUseCaseTests
{
    private const string Primary = "primary";
    private static readonly Guid AttemptId = Guid.NewGuid();
    private readonly Fakes fakes = new();
    private readonly Guid admin;
    private readonly Guid student;

    public TransferAndPlacementUseCaseTests()
    {
        admin = fakes.AddPerson("Marta", "Administradora", Roles.Administrator);
        student = fakes.AddPerson("Ana", "Pérez", Roles.Student);
    }

    private EnrollmentTransferUseCases Transfers() => new(fakes, fakes, fakes, TimeProvider.System);
    private PlacementUseCases Placement() => new(fakes, fakes, fakes, fakes, fakes, fakes);

    // ─── Cambio de grupo ───────────────────────────────────────────────

    [Fact]
    public async Task Transfer_MovesTheSeat_AndRecordsWhoWhenAndWhy()
    {
        var from = fakes.AddGroup("Matemáticas · Primaria Prebásico", "Grupo A", 8);
        var to = fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 8);
        var seat = fakes.Enroll(student, from, activate: true);

        var result = await Transfers().TransferAsync(admin, seat.Id, to, " El diagnóstico confirmó Básico ", default);

        Assert.Equal(EnrollmentResultStatus.Ok, result.Status);
        var view = result.Value!;
        Assert.Equal(("Ana Pérez", "Grupo A", "Grupo B", "El diagnóstico confirmó Básico", "Marta Administradora", seat.Id),
            (view.StudentName, view.FromOfferingName, view.ToOfferingName, view.Reason, view.TransferredByName, view.FromEnrollmentId));
        Assert.Equal(EnrollmentStatus.Withdrawn, seat.Status);
        Assert.Equal(EnrollmentStatus.Active, fakes.Seats.Single(item => item.Id == view.ToEnrollmentId).Status);
    }

    [Fact]
    public async Task Transfer_RespectsTheCapacityOfTheDestination()
    {
        var from = fakes.AddGroup("Matemáticas · Primaria Prebásico", "Grupo A", 8);
        var full = fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 1);
        fakes.Enroll(fakes.AddPerson("Luis", "Rojas", Roles.Student), full);
        var seat = fakes.Enroll(student, from);

        var result = await Transfers().TransferAsync(admin, seat.Id, full, null, default);

        Assert.Equal(EnrollmentResultStatus.Conflict, result.Status);
        Assert.Contains("lleno (1 de 1", result.Detail);
        Assert.Equal(EnrollmentStatus.Pending, seat.Status);
        Assert.Empty(fakes.Transfers);
    }

    [Fact]
    public async Task Transfer_RejectsDraftsUnknownGroupsSameGroupAndAlreadyEnrolled()
    {
        var from = fakes.AddGroup("Matemáticas · Primaria Prebásico", "Grupo A", 8);
        var draft = fakes.AddGroup("Matemáticas · Primaria Básico", "Borrador", 8, published: false);
        var held = fakes.AddGroup("Matemáticas · Primaria Intermedio", "Grupo C", 8);
        var seat = fakes.Enroll(student, from);
        fakes.Enroll(student, held);

        Assert.Equal(EnrollmentResultStatus.Conflict, (await Transfers().TransferAsync(admin, seat.Id, draft, null, default)).Status);
        Assert.Equal(EnrollmentResultStatus.NotFound, (await Transfers().TransferAsync(admin, seat.Id, Guid.NewGuid(), null, default)).Status);
        Assert.Equal(EnrollmentResultStatus.Invalid, (await Transfers().TransferAsync(admin, seat.Id, from, null, default)).Status);
        Assert.Equal(EnrollmentResultStatus.Conflict, (await Transfers().TransferAsync(admin, seat.Id, held, null, default)).Status);
        Assert.Equal(EnrollmentStatus.Pending, seat.Status);
        Assert.Empty(fakes.Transfers);
    }

    [Fact]
    public async Task Transfer_RejectsUnknownOrWithdrawnSeats_AndLongReasons()
    {
        var from = fakes.AddGroup("Matemáticas · Primaria Prebásico", "Grupo A", 8);
        var to = fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 8);
        var seat = fakes.Enroll(student, from);

        Assert.Equal(EnrollmentResultStatus.NotFound, (await Transfers().TransferAsync(admin, Guid.NewGuid(), to, null, default)).Status);
        Assert.Equal(EnrollmentResultStatus.Invalid,
            (await Transfers().TransferAsync(admin, seat.Id, to, new string('x', EnrollmentTransfer.MaxReasonLength + 1), default)).Status);
        seat.Withdraw(admin, DateTimeOffset.UtcNow);
        Assert.Equal(EnrollmentResultStatus.Conflict, (await Transfers().TransferAsync(admin, seat.Id, to, null, default)).Status);
    }

    [Fact]
    public async Task TheChangesAreListedNewestFirst_WithNames_AndFilteredByStudent()
    {
        var a = fakes.AddGroup("Matemáticas · Primaria Prebásico", "Grupo A", 8);
        var b = fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 8);
        var c = fakes.AddGroup("Matemáticas · Primaria Intermedio", "Grupo C", 8);
        var first = fakes.Enroll(student, a);
        var other = fakes.Enroll(fakes.AddPerson("Luis", "Rojas", Roles.Student), a);
        var moved = (await Transfers().TransferAsync(admin, first.Id, b, "Subió de nivel", default)).Value!;
        await Transfers().TransferAsync(admin, moved.ToEnrollmentId, c, null, default);
        await Transfers().TransferAsync(admin, other.Id, b, null, default);

        var page = await Transfers().ListAsync(new TransferListQuery(student, null), default);

        Assert.Equal(2, page.Total);
        Assert.Equal(["Grupo C", "Grupo B"], page.Items.Select(item => item.ToOfferingName));
        Assert.All(page.Items, item => Assert.Equal("Ana Pérez", item.StudentName));
        Assert.Equal(3, (await Transfers().ListAsync(new TransferListQuery(null, null), default)).Total);
    }

    // ─── Sugerencia de grupos ──────────────────────────────────────────

    [Fact]
    public async Task Placement_PutsFirstTheGroupsOfTheConfirmedLevel_WithTheirFreeSeats()
    {
        fakes.AddGroup("Matemáticas · Primaria Prebásico", "Grupo A", 8, level: (Primary, "prebasic"));
        fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 8, level: (Primary, "basic"));
        var basic2 = fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo C", 2, level: (Primary, "basic"));
        fakes.Enroll(fakes.AddPerson("Luis", "Rojas", Roles.Student), basic2);
        fakes.Diagnostics[student] = [new DiagnosticLevelInfo(Primary, "completed", "prebasic", "basic", AttemptId)];

        var placement = (await Placement().GetAsync(student, default)).Value!;

        Assert.Equal(["Grupo B", "Grupo C", "Grupo A"], placement.Groups.Select(group => group.OfferingName));
        Assert.Equal([true, true, false], placement.Groups.Select(group => group.Recommended));
        Assert.Equal([8, 1, 8], placement.Groups.Select(group => group.FreeSeats));
        Assert.Equal(("Primaria", "Básico"), (placement.Groups[0].StageLabel, placement.Groups[0].LevelLabel));
        var diagnostic = Assert.Single(placement.Diagnostics);
        Assert.Equal(("Primaria", "Prebásico", "Básico", AttemptId), (diagnostic.StageLabel, diagnostic.Suggested!.Label, diagnostic.Confirmed!.Label, diagnostic.AttemptId));
    }

    [Fact]
    public async Task Placement_WithoutAConfirmedLevel_RecommendsNothing_ButShowsTheSuggestedOne()
    {
        fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 8, level: (Primary, "basic"));
        fakes.Diagnostics[student] = [new DiagnosticLevelInfo(Primary, "completed", "basic", null, AttemptId)];

        var placement = (await Placement().GetAsync(student, default)).Value!;

        Assert.All(placement.Groups, group => Assert.False(group.Recommended));
        Assert.Equal(("basic", null), (placement.Diagnostics[0].Suggested!.Code, placement.Diagnostics[0].Confirmed));
    }

    [Fact]
    public async Task Placement_OnlyMatchesTheStageOfTheDiagnostic()
    {
        fakes.AddGroup("Matemáticas · Secundaria Básico", "Grupo S", 8, level: ("secondary", "basic"));
        fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo P", 8, level: (Primary, "basic"));
        fakes.Diagnostics[student] = [new DiagnosticLevelInfo(Primary, "completed", "basic", "basic", AttemptId)];

        var placement = (await Placement().GetAsync(student, default)).Value!;

        Assert.Equal(["Grupo P", "Grupo S"], placement.Groups.Select(group => group.OfferingName));
        Assert.Equal([true, false], placement.Groups.Select(group => group.Recommended));
    }

    [Fact]
    public async Task Placement_LeavesOutDraftsAndCoursesThatAreNotLevels_AndFlagsTheSeatsTheStudentHolds()
    {
        var mine = fakes.AddGroup("Matemáticas · Primaria Básico", "Grupo B", 8, level: (Primary, "basic"));
        fakes.AddGroup("Matemáticas · Primaria Básico", "Borrador", 8, level: (Primary, "basic"), published: false);
        fakes.AddGroup("Laboratorio de Roblox", "Grupo Lab", 8);
        fakes.Enroll(student, mine, activate: true);

        var placement = (await Placement().GetAsync(student, default)).Value!;

        var group = Assert.Single(placement.Groups);
        Assert.Equal((mine, true, 7), (group.OfferingId, group.AlreadyEnrolled, group.FreeSeats));
        var held = Assert.Single(placement.Enrollments);
        Assert.Equal(("Grupo B", "active"), (held.OfferingName, held.Status));
    }

    [Fact]
    public async Task Placement_OnlyForExistingStudents()
    {
        Assert.Equal(EnrollmentResultStatus.NotFound, (await Placement().GetAsync(Guid.NewGuid(), default)).Status);
        Assert.Equal(EnrollmentResultStatus.Invalid, (await Placement().GetAsync(admin, default)).Status);
    }

    private sealed class Fakes : IEnrollmentRepository, IEnrollmentTransferRepository, IOfferingDirectory, IMathLevelDirectory,
        IDiagnosticLevelDirectory, IUserDirectory, IRoleDirectory
    {
        private readonly List<OfferingSummary> offerings = [];
        private readonly Dictionary<Guid, LevelOfCourse> levels = [];
        private readonly List<UserSummary> people = [];
        private readonly Dictionary<Guid, IReadOnlyList<string>> roles = [];
        public List<Enrollment> Seats { get; } = [];
        public List<EnrollmentTransfer> Transfers { get; } = [];
        public Dictionary<Guid, IReadOnlyList<DiagnosticLevelInfo>> Diagnostics { get; } = [];

        public Guid AddPerson(string given, string surname, string role)
        {
            var id = Guid.NewGuid();
            people.Add(new UserSummary(id, given, surname, null, null, "DNI", "70000000", DateTimeOffset.UtcNow, 0));
            roles[id] = [role];
            return id;
        }

        /// <param name="level">The stage and level code when the course is a Mathematics level.</param>
        public Guid AddGroup(string course, string name, int capacity, bool published = true, (string Stage, string Level)? level = null)
        {
            var id = Guid.NewGuid();
            var courseId = Guid.NewGuid();
            offerings.Add(new OfferingSummary(id, courseId, course, name, capacity, published));
            if (level is { } known)
                levels[courseId] = new LevelOfCourse(courseId, known.Stage, known.Level,
                    known.Stage == Primary ? "Primaria" : "Secundaria", known.Level switch
                    {
                        "prebasic" => "Prebásico",
                        "basic" => "Básico",
                        "intermediate" => "Intermedio",
                        _ => "Avanzado"
                    });
            return id;
        }

        public Enrollment Enroll(Guid studentUserId, Guid offeringId, bool activate = false)
        {
            var seat = Enrollment.Create(Guid.NewGuid(), studentUserId, offeringId, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
            if (activate) seat.Activate(Guid.NewGuid(), null, DateTimeOffset.UtcNow);
            Seats.Add(seat);
            return seat;
        }

        // IEnrollmentRepository (only what these use cases read)
        public Task<IReadOnlyList<Enrollment>> GetLiveByStudentsAsync(IReadOnlyCollection<Guid> studentUserIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(Seats.Where(seat => studentUserIds.Contains(seat.StudentUserId) && seat.HoldsSeat).ToList());

        public Task<IReadOnlyDictionary<Guid, int>> CountSeatsAsync(IReadOnlyCollection<Guid>? offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(Seats.Where(seat => seat.HoldsSeat && (offeringIds is null || offeringIds.Contains(seat.CourseOfferingId)))
                .GroupBy(seat => seat.CourseOfferingId).ToDictionary(group => group.Key, group => group.Count()));

        public Task<Enrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<Enrollment> Items, int Total)> SearchAsync(EnrollmentQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SeatReservation> AddWithinCapacityAsync(Enrollment enrollment, int maximumCapacity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetActiveOfferingIdsAsync(Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        // IEnrollmentTransferRepository: the same rules as the transaction in PostgreSQL
        public Task<TransferAttempt> TransferAsync(Guid enrollmentId, Guid destinationOfferingId, int destinationCapacity, Guid actorUserId,
            string? reason, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var source = Seats.SingleOrDefault(seat => seat.Id == enrollmentId);
            if (source is null) return Task.FromResult(new TransferAttempt(TransferOutcome.SourceNotFound));
            if (!source.HoldsSeat) return Task.FromResult(new TransferAttempt(TransferOutcome.SourceWithdrawn));
            if (source.CourseOfferingId == destinationOfferingId) return Task.FromResult(new TransferAttempt(TransferOutcome.SameGroup));
            var live = Seats.Where(seat => seat.CourseOfferingId == destinationOfferingId && seat.HoldsSeat).ToList();
            if (live.Any(seat => seat.StudentUserId == source.StudentUserId)) return Task.FromResult(new TransferAttempt(TransferOutcome.AlreadyEnrolled));
            if (live.Count >= destinationCapacity) return Task.FromResult(new TransferAttempt(TransferOutcome.Full));

            var moved = source.TransferTo(Guid.NewGuid(), destinationOfferingId, actorUserId, now);
            var record = EnrollmentTransfer.Record(Guid.NewGuid(), source, moved, reason, actorUserId, now);
            Seats.Add(moved);
            Transfers.Add(record);
            return Task.FromResult(new TransferAttempt(TransferOutcome.Transferred, moved, record));
        }

        public Task<(IReadOnlyList<EnrollmentTransfer> Items, int Total)> SearchTransfersAsync(Guid? studentUserId, Guid? courseOfferingId,
            int page, int pageSize, CancellationToken cancellationToken)
        {
            var matches = Transfers.Where(item => (studentUserId is null || item.StudentUserId == studentUserId) &&
                                                  (courseOfferingId is null || item.FromOfferingId == courseOfferingId || item.ToOfferingId == courseOfferingId))
                .Reverse().ToList();
            return Task.FromResult<(IReadOnlyList<EnrollmentTransfer>, int)>((matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matches.Count));
        }

        // IOfferingDirectory
        Task<OfferingSummary?> IOfferingDirectory.GetAsync(Guid offeringId, CancellationToken cancellationToken) =>
            Task.FromResult(offerings.SingleOrDefault(offering => offering.Id == offeringId));
        Task<IReadOnlyList<OfferingSummary>> IOfferingDirectory.GetManyAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(offerings.Where(offering => offeringIds.Contains(offering.Id)).ToList());
        Task<IReadOnlyList<OfferingSummary>> IOfferingDirectory.ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(offerings.ToList());

        // IMathLevelDirectory
        public Task<IReadOnlyList<LevelOfCourse>> GetLevelsAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelOfCourse>>(levels.Where(pair => courseIds.Contains(pair.Key)).Select(pair => pair.Value).ToList());

        // IDiagnosticLevelDirectory
        Task<IReadOnlyList<DiagnosticLevelInfo>> IDiagnosticLevelDirectory.GetAsync(Guid studentUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Diagnostics.GetValueOrDefault(studentUserId) ?? []);

        // IUserDirectory
        Task<UserSummary?> IUserDirectory.GetAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(people.SingleOrDefault(person => person.Id == userId));
        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSummary>>(people.Where(person => userIds.Contains(person.Id)).ToList());
        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) => throw new NotSupportedException();

        // IRoleDirectory
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(roles.Where(pair => userIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
        public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

// The change-of-group queries translate to PostgreSQL in one statement each.
public sealed class EnrollmentTransferQueryTests
{
    private readonly SqlCapture capture = new();

    private EnrollmentRepository Repository() => new(new EnrollmentsDbContext(
        new DbContextOptionsBuilder<EnrollmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model")
            .AddInterceptors(capture)
            .Options));

    [Fact]
    public async Task Search_FiltersByStudentAndEitherSideOfTheMove_AndPagesInSql()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().SearchTransfersAsync(Guid.NewGuid(), Guid.NewGuid(), 1, 20, default));

        Assert.Contains("student_user_id", sql);
        Assert.Contains("from_offering_id", sql);
        Assert.Contains("to_offering_id", sql);
        Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase);
    }

    // The move itself (two advisory locks + checks + insert + update in one transaction) needs a real transaction:
    // it is covered against PostgreSQL, with concurrent requests, by tests/e2e/placement.mjs.
}
