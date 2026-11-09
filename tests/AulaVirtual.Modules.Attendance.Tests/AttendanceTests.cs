using AulaVirtual.Modules.Attendance.Application;
using AulaVirtual.Modules.Attendance.Domain;
using AulaVirtual.Modules.Attendance.Infrastructure.Persistence;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Attendance.Tests;

public sealed class AttendanceRecordTests
{
    [Fact]
    public void Change_RecordsWhoCorrected_AndIgnoresAResubmittedSameValue()
    {
        var teacher = Guid.NewGuid();
        var record = AttendanceRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Guid.NewGuid(), AttendanceStatus.Absent, null, teacher, DateTimeOffset.UtcNow);

        Assert.False(record.Change(AttendanceStatus.Absent, "  ", teacher, DateTimeOffset.UtcNow));
        Assert.Null(record.UpdatedByUserId);

        var admin = Guid.NewGuid();
        Assert.True(record.Change(AttendanceStatus.Late, " Entró 10 min tarde ", admin, DateTimeOffset.UtcNow));
        Assert.Equal(AttendanceStatus.Late, record.Status);
        Assert.Equal("Entró 10 min tarde", record.Note);
        Assert.Equal(admin, record.UpdatedByUserId);
    }

    [Theory]
    [InlineData("present", AttendanceStatus.Present)]
    [InlineData(" LATE ", AttendanceStatus.Late)]
    [InlineData("absent", AttendanceStatus.Absent)]
    public void StatusCodes_RoundTrip(string code, AttendanceStatus expected)
    {
        Assert.True(AttendanceStatusCodes.TryParse(code, out var status));
        Assert.Equal(expected, status);
        Assert.Equal(code.Trim().ToLowerInvariant(), AttendanceStatusCodes.ToCode(status));
    }

    [Fact]
    public void Summary_RateCountsLateAsAttended()
    {
        Assert.Equal(75, AttendanceSummary.From(2, 1, 1).Rate);
        Assert.Null(AttendanceSummary.From(0, 0, 0).Rate);
    }
}

public sealed class AttendanceUseCaseTests
{
    private readonly Fakes fakes = new();
    private readonly Guid teacher = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid stranger = Guid.NewGuid();
    private readonly Guid ana = Guid.NewGuid();
    private readonly Guid luis = Guid.NewGuid();
    private readonly Guid group = Guid.NewGuid();
    private readonly Guid session = Guid.NewGuid();

    public AttendanceUseCaseTests()
    {
        fakes.Sessions[session] = new SessionDetails(session, group, "Grupo A", "Matemáticas",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(85), true, [teacher]);
        fakes.Admins.Add(admin);
        fakes.Roster[group] = [ana, luis];
        fakes.AddPerson(ana, "Ana", "Quispe");
        fakes.AddPerson(luis, "Luis", "Rojas");
    }

    private AttendanceUseCases UseCases() => new(fakes, fakes, fakes, fakes, fakes, TimeProvider.System);

    private static AttendanceEntry Entry(Guid student, string status, string? note = null) => new(student, status, note);

    [Fact]
    public async Task TeacherOfTheGroup_MarksTheWholeRoster_InOneSave()
    {
        var result = await UseCases().MarkAsync(teacher, session, [Entry(ana, "present"), Entry(luis, "late", "10 min")], default);

        Assert.Equal(AttendanceResultStatus.Ok, result.Status);
        Assert.Equal(1, fakes.Saves);
        Assert.Equal(["Ana Quispe", "Luis Rojas"], result.Value!.Students.Select(s => s.FullName));
        Assert.Equal(["present", "late"], result.Value.Students.Select(s => s.Status));
        Assert.All(fakes.Records, record => Assert.Equal(group, record.CourseOfferingId));
    }

    [Fact]
    public async Task Administrator_CanMark_OtherAccountsCannot()
    {
        Assert.Equal(AttendanceResultStatus.Ok, (await UseCases().MarkAsync(admin, session, [Entry(ana, "absent")], default)).Status);
        Assert.Equal(AttendanceResultStatus.Forbidden, (await UseCases().MarkAsync(stranger, session, [Entry(ana, "present")], default)).Status);
        Assert.Equal(AttendanceResultStatus.Forbidden, (await UseCases().GetSessionAsync(stranger, session, default)).Status);
    }

    [Fact]
    public async Task Correction_UpdatesTheSameRecord()
    {
        await UseCases().MarkAsync(teacher, session, [Entry(ana, "absent")], default);
        var result = await UseCases().MarkAsync(admin, session, [Entry(ana, "present", "Llegó con el profesor")], default);

        var record = Assert.Single(fakes.Records);
        Assert.Equal(AttendanceStatus.Present, record.Status);
        Assert.Equal(admin, record.UpdatedByUserId);
        Assert.Equal("Llegó con el profesor", result.Value!.Students.Single(s => s.StudentUserId == ana).Note);
    }

    [Fact]
    public async Task OnlyActiveStudents_CanBeMarked()
    {
        var result = await UseCases().MarkAsync(teacher, session, [Entry(Guid.NewGuid(), "present")], default);
        Assert.Equal(AttendanceResultStatus.Invalid, result.Status);
        Assert.Empty(fakes.Records);
    }

    [Theory]
    [InlineData("presente")]
    [InlineData("")]
    public async Task UnknownStatus_IsRejected(string status) =>
        Assert.Equal(AttendanceResultStatus.Invalid, (await UseCases().MarkAsync(teacher, session, [Entry(ana, status)], default)).Status);

    [Fact]
    public async Task DuplicatedOrEmptyRoster_IsRejected()
    {
        Assert.Equal(AttendanceResultStatus.Invalid, (await UseCases().MarkAsync(teacher, session, [], default)).Status);
        Assert.Equal(AttendanceResultStatus.Invalid,
            (await UseCases().MarkAsync(teacher, session, [Entry(ana, "present"), Entry(ana, "absent")], default)).Status);
    }

    [Fact]
    public async Task FutureClass_OpensFifteenMinutesBefore()
    {
        var later = Guid.NewGuid();
        fakes.Sessions[later] = fakes.Sessions[session] with
        {
            SessionId = later, StartsAt = DateTimeOffset.UtcNow.AddHours(2), EndsAt = DateTimeOffset.UtcNow.AddHours(3)
        };

        var result = await UseCases().MarkAsync(teacher, later, [Entry(ana, "present")], default);
        var view = await UseCases().GetSessionAsync(teacher, later, default);

        Assert.Equal(AttendanceResultStatus.Conflict, result.Status);
        Assert.False(view.Value!.CanMark);
    }

    [Fact]
    public async Task DraftOrUnknownClass_IsNotFound()
    {
        var draft = Guid.NewGuid();
        fakes.Sessions[draft] = fakes.Sessions[session] with { SessionId = draft, IsPublished = false };

        Assert.Equal(AttendanceResultStatus.NotFound, (await UseCases().GetSessionAsync(admin, draft, default)).Status);
        Assert.Equal(AttendanceResultStatus.NotFound, (await UseCases().GetSessionAsync(admin, Guid.NewGuid(), default)).Status);
    }

    [Fact]
    public async Task ConcurrentSave_IsRetriedOnce_ThenReported()
    {
        fakes.FailNextSaves = 1;
        Assert.Equal(AttendanceResultStatus.Ok, (await UseCases().MarkAsync(teacher, session, [Entry(ana, "present")], default)).Status);

        fakes.FailNextSaves = 2;
        Assert.Equal(AttendanceResultStatus.Conflict, (await UseCases().MarkAsync(teacher, session, [Entry(luis, "present")], default)).Status);
    }

    [Fact]
    public async Task StudentWhoLeft_KeepsTheirRecord_ButIsNoLongerEnrolled()
    {
        await UseCases().MarkAsync(teacher, session, [Entry(luis, "absent")], default);
        fakes.Roster[group] = [ana];

        var view = (await UseCases().GetSessionAsync(teacher, session, default)).Value!;

        var row = view.Students.Single(s => s.StudentUserId == luis);
        Assert.False(row.Enrolled);
        Assert.Equal("absent", row.Status);
        Assert.Null(view.Students.Single(s => s.StudentUserId == ana).Status);
    }

    [Fact]
    public async Task Family_SeesOnlyTheirChildren_WithTotalsAndGroupNames()
    {
        var parent = Guid.NewGuid();
        fakes.Links[parent] = [ana];
        await UseCases().MarkAsync(teacher, session, [Entry(ana, "late"), Entry(luis, "absent")], default);

        var reports = await new StudentAttendanceUseCases(fakes, fakes, fakes, TimeProvider.System).ForGuardianAsync(parent, default);

        var report = Assert.Single(reports);
        Assert.Equal(ana, report.StudentUserId);
        Assert.Equal(1, report.Summary.Late);
        Assert.Equal(100, report.Summary.Rate);
        Assert.Equal("Matemáticas", Assert.Single(report.Recent).CourseTitle);
    }

    [Fact]
    public async Task GuardianWithoutChildren_GetsAnEmptyList_WithoutQueryingRecords()
    {
        var reports = await new StudentAttendanceUseCases(fakes, fakes, fakes, TimeProvider.System).ForGuardianAsync(Guid.NewGuid(), default);
        Assert.Empty(reports);
        Assert.Equal(0, fakes.ReportQueries);
    }

    private sealed class Fakes : IOfferingScheduleDirectory, IPermissionChecker, IEnrollmentRoster, IAttendanceRepository,
        IUserDirectory, IGuardianLinks, IOfferingDirectory
    {
        public Dictionary<Guid, SessionDetails> Sessions { get; } = [];
        public HashSet<Guid> Admins { get; } = [];
        public Dictionary<Guid, IReadOnlyList<Guid>> Roster { get; } = [];
        public Dictionary<Guid, IReadOnlyList<Guid>> Links { get; } = [];
        public List<AttendanceRecord> Records { get; } = [];
        private readonly List<AttendanceRecord> pending = [];
        private readonly List<UserSummary> people = [];
        public int Saves { get; private set; }
        public int FailNextSaves { get; set; }
        public int ReportQueries { get; private set; }

        public void AddPerson(Guid id, string given, string surname) =>
            people.Add(new UserSummary(id, given, surname, null, null, null, null, DateTimeOffset.UtcNow, 0));

        public Task<SessionDetails?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.GetValueOrDefault(sessionId));

        public Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(permissionCode == Permissions.AttendanceManage && Admins.Contains(userId));

        public Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid courseOfferingId, CancellationToken cancellationToken) =>
            Task.FromResult(Roster.GetValueOrDefault(courseOfferingId) ?? []);

        public Task<IReadOnlyList<AttendanceRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AttendanceRecord>>(Records.Where(r => r.SessionId == sessionId).ToList());

        public Task<IReadOnlyList<AttendanceRecord>> GetTrackedBySessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            GetBySessionAsync(sessionId, cancellationToken);

        public void Add(AttendanceRecord record) => pending.Add(record);

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
        {
            if (FailNextSaves > 0)
            {
                FailNextSaves--;
                pending.Clear();
                return Task.FromResult(false);
            }

            Records.AddRange(pending);
            pending.Clear();
            Saves++;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<StatusCount>> CountByStatusAsync(IReadOnlyCollection<Guid> studentUserIds, CancellationToken cancellationToken)
        {
            ReportQueries++;
            return Task.FromResult<IReadOnlyList<StatusCount>>(Records.Where(r => studentUserIds.Contains(r.StudentUserId))
                .GroupBy(r => (r.StudentUserId, r.Status)).Select(g => new StatusCount(g.Key.StudentUserId, g.Key.Status, g.Count())).ToList());
        }

        public Task<IReadOnlyList<AttendanceRecord>> GetRecentAsync(IReadOnlyCollection<Guid> studentUserIds, DateTimeOffset since, int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AttendanceRecord>>(Records.Where(r => studentUserIds.Contains(r.StudentUserId) && r.SessionStartsAt >= since)
                .OrderByDescending(r => r.SessionStartsAt).Take(limit).ToList());

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSummary>>(people.Where(p => userIds.Contains(p.Id)).ToList());

        public Task<IReadOnlyList<Guid>> GetStudentIdsAsync(Guid guardianUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Links.GetValueOrDefault(guardianUserId) ?? []);

        Task<IReadOnlyList<OfferingSummary>> IOfferingDirectory.GetManyAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OfferingSummary>>(Sessions.Values.Select(s => s.OfferingId).Distinct().Where(offeringIds.Contains)
                .Select(id => new OfferingSummary(id, Guid.NewGuid(), "Matemáticas", "Grupo A", 8, true)).ToList());

        public Task<IReadOnlyList<ScheduledSession>> GetSessionsAsync(IReadOnlyCollection<Guid> offeringIds, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<OfferingSummary?> IOfferingDirectory.GetAsync(Guid offeringId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OfferingSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<UserSummary?> IUserDirectory.GetAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> LinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UnlinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetGuardianIdsAsync(Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

// Every query translates to PostgreSQL and uses the indexes in the model.
public sealed class AttendanceQueryTests
{
    private readonly SqlCapture capture = new();

    private AttendanceRepository Repository() => new(new AttendanceDbContext(new DbContextOptionsBuilder<AttendanceDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options));

    [Fact]
    public async Task Totals_AreOneGroupedQuery()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().CountByStatusAsync([Guid.NewGuid(), Guid.NewGuid()], default));
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("= ANY (", sql);
    }

    [Fact]
    public async Task Recent_IsBoundedAndNewestFirst()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().GetRecentAsync([Guid.NewGuid()], DateTimeOffset.UtcNow.AddDays(-30), 50, default));
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("DESC", sql);
        Assert.Contains("LIMIT", sql);
    }

    [Fact]
    public void Model_HasUniqueClassStudent_AndStudentHistoryIndexes()
    {
        using var db = new AttendanceDbContext(new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model").Options);
        var indexes = db.Model.FindEntityType(typeof(AttendanceRecord))!.GetIndexes().ToArray();

        Assert.Contains(indexes, index => index.GetDatabaseName() == "ux_attendance_records_session_student" && index.IsUnique);
        Assert.Contains(indexes, index => index.GetDatabaseName() == "ix_attendance_records_student_starts_at");
    }
}
