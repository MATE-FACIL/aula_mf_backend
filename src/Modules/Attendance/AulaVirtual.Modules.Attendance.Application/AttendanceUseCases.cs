using AulaVirtual.Modules.Attendance.Domain;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Attendance.Application;

// Taking attendance for one class (SCRUM-53). Allowed for an administrator (attendance.manage) or a teacher of
// that class's group, from shortly before the class starts. Only students with an ACTIVE enrollment can be
// marked; the whole roster is saved in one request and one transaction.
//
// Fixed cost per request, whatever the class size: class + teachers (2) and the permission (1) at the same
// time, then roster (1) and records (1) at the same time, then names (1). Different modules use different
// DbContexts, so those pairs really run in parallel.
public sealed class AttendanceUseCases(
    IOfferingScheduleDirectory schedule,
    IPermissionChecker permissions,
    IEnrollmentRoster roster,
    IAttendanceRepository records,
    IUserDirectory users,
    TimeProvider timeProvider)
{
    public const int MaxEntries = 100;
    public static readonly TimeSpan OpensBeforeStart = TimeSpan.FromMinutes(15);

    private const string SessionNotFound = "No encontramos esa clase.";

    public async Task<AttendanceResult<SessionAttendanceView>> GetSessionAsync(Guid actorUserId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(actorUserId, sessionId, cancellationToken);
        if (access.Status != AttendanceResultStatus.Ok) return Fail<SessionAttendanceView>(access);
        return AttendanceResult<SessionAttendanceView>.Ok(await BuildViewAsync(access.Value!, cancellationToken));
    }

    public async Task<AttendanceResult<SessionAttendanceView>> MarkAsync(Guid actorUserId, Guid sessionId,
        IReadOnlyList<AttendanceEntry>? entries, CancellationToken cancellationToken)
    {
        if (entries is null || entries.Count == 0)
            return AttendanceResult<SessionAttendanceView>.Invalid("Marca al menos a un alumno.");
        if (entries.Count > MaxEntries)
            return AttendanceResult<SessionAttendanceView>.Invalid($"Se pueden marcar como máximo {MaxEntries} alumnos a la vez.");
        if (entries.Select(entry => entry.StudentUserId).Distinct().Count() != entries.Count)
            return AttendanceResult<SessionAttendanceView>.Invalid("Cada alumno debe aparecer una sola vez.");

        var parsed = new List<(Guid Student, AttendanceStatus Status, string? Note)>(entries.Count);
        foreach (var entry in entries)
        {
            if (!AttendanceStatusCodes.TryParse(entry.Status, out var status))
                return AttendanceResult<SessionAttendanceView>.Invalid(
                    $"Estado desconocido: «{entry.Status}». Usa «present», «late» o «absent».");
            if (entry.Note?.Trim().Length > AttendanceRecord.MaxNoteLength)
                return AttendanceResult<SessionAttendanceView>.Invalid(
                    $"La nota admite como máximo {AttendanceRecord.MaxNoteLength} caracteres.");
            parsed.Add((entry.StudentUserId, status, entry.Note));
        }

        var access = await AuthorizeAsync(actorUserId, sessionId, cancellationToken);
        if (access.Status != AttendanceResultStatus.Ok) return Fail<SessionAttendanceView>(access);
        var session = access.Value!;

        var now = timeProvider.GetUtcNow();
        if (now < session.StartsAt - OpensBeforeStart)
            return AttendanceResult<SessionAttendanceView>.Conflict(
                "Todavía no empieza la clase: la asistencia se abre 15 minutos antes.");

        var enrolled = (await roster.GetActiveStudentIdsAsync(session.OfferingId, cancellationToken)).ToHashSet();
        if (parsed.Any(entry => !enrolled.Contains(entry.Student)))
            return AttendanceResult<SessionAttendanceView>.Invalid(
                "Solo se puede marcar a alumnos con matrícula activa en este grupo.");

        // Two people may save the same class at once: the unique (class, student) index stops a duplicate,
        // and the second save reads the first one's rows and applies its changes on top.
        for (var attempt = 1; ; attempt++)
        {
            var existing = (await records.GetTrackedBySessionAsync(session.SessionId, cancellationToken))
                .ToDictionary(record => record.StudentUserId);
            foreach (var (student, status, note) in parsed)
            {
                if (existing.TryGetValue(student, out var record)) record.Change(status, note, actorUserId, now);
                else records.Add(AttendanceRecord.Create(Guid.NewGuid(), session.SessionId, session.OfferingId,
                    session.StartsAt, student, status, note, actorUserId, now));
            }

            if (await records.TrySaveChangesAsync(cancellationToken)) break;
            if (attempt == 2)
                return AttendanceResult<SessionAttendanceView>.Conflict(
                    "Otra persona está guardando esta asistencia. Vuelve a intentarlo.");
        }

        return AttendanceResult<SessionAttendanceView>.Ok(await BuildViewAsync(session, cancellationToken));
    }

    private async Task<AttendanceResult<SessionDetails>> AuthorizeAsync(Guid actorUserId, Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessionTask = schedule.GetSessionAsync(sessionId, cancellationToken);
        var permissionTask = permissions.HasPermissionAsync(actorUserId, Permissions.AttendanceManage, cancellationToken);
        await Task.WhenAll(sessionTask, permissionTask);

        // Same answer for a class that does not exist and one in a draft group: drafts have no attendance yet.
        if (sessionTask.Result is not { IsPublished: true } session)
            return AttendanceResult<SessionDetails>.NotFound(SessionNotFound);
        if (!permissionTask.Result && !session.InstructorUserIds.Contains(actorUserId))
            return AttendanceResult<SessionDetails>.Forbidden("Solo el profesor del grupo o el equipo pueden tomar esta asistencia.");
        return AttendanceResult<SessionDetails>.Ok(session);
    }

    private async Task<SessionAttendanceView> BuildViewAsync(SessionDetails session, CancellationToken cancellationToken)
    {
        var rosterTask = roster.GetActiveStudentIdsAsync(session.OfferingId, cancellationToken);
        var recordsTask = records.GetBySessionAsync(session.SessionId, cancellationToken);
        await Task.WhenAll(rosterTask, recordsTask);

        var enrolled = rosterTask.Result.ToHashSet();
        var marked = recordsTask.Result.ToDictionary(record => record.StudentUserId);
        var people = (await users.GetManyAsync(enrolled.Union(marked.Keys).ToArray(), cancellationToken))
            .ToDictionary(person => person.Id);

        var students = enrolled.Union(marked.Keys)
            .Where(people.ContainsKey)
            .Select(id =>
            {
                var record = marked.GetValueOrDefault(id);
                return new AttendanceStudentRow(id, people[id].FullName,
                    record is null ? null : AttendanceStatusCodes.ToCode(record.Status), record?.Note,
                    record?.UpdatedAt ?? record?.RecordedAt, enrolled.Contains(id));
            })
            .OrderBy(row => row.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var opensAt = session.StartsAt - OpensBeforeStart;
        return new SessionAttendanceView(session.SessionId, session.OfferingId, session.CourseTitle, session.OfferingName,
            session.StartsAt, session.EndsAt, opensAt, timeProvider.GetUtcNow() >= opensAt, students);
    }

    private static AttendanceResult<T> Fail<T>(AttendanceResult<SessionDetails> failure) =>
        new(failure.Status, default, failure.Detail);
}
