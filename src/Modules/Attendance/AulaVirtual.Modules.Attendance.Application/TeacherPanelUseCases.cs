using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Exams.Application.Practice;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Attendance.Application;

/// <summary>One of the teacher's groups on the panel's front page.</summary>
public sealed record TeacherGroupView(
    Guid OfferingId,
    Guid CourseId,
    string CourseTitle,
    string OfferingName,
    bool IsPublished,
    int ActiveStudents,
    int MaximumCapacity,
    int Sessions,
    Guid? NextSessionId,
    DateTimeOffset? NextSessionStartsAt);

/// <summary>A class of the group with how its attendance went (Marked = present + late + absent).</summary>
public sealed record TeacherSessionView(
    Guid SessionId,
    int Number,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? VirtualLink,
    int Present,
    int Late,
    int Absent,
    int Marked);

/// <summary>A student of the group: their attendance in this group and their practice progress in the course.</summary>
public sealed record TeacherStudentView(
    Guid StudentUserId,
    string FullName,
    AttendanceSummary Attendance,
    Progress Practice);

public sealed record TeacherGroupDetail(
    TeacherGroupView Group,
    IReadOnlyList<TeacherSessionView> Sessions,
    IReadOnlyList<TeacherStudentView> Students);

// The teacher panel (SCRUM-69): only the groups the signed-in teacher is assigned to («ve solo sus grupos»), each with
// its classes, attendance and students' progress. Fixed cost per request, whatever the size of the group:
//   groups: groups + classes (2) and active counts (1);
//   one group: groups (2), classes (2), roster (1), marks per class (1) and per student (1), names (1), practice (3).
public sealed class TeacherPanelUseCases(
    ITeachingDirectory teaching,
    IEnrollmentRoster roster,
    IAttendanceRepository records,
    IUserDirectory users,
    IPracticeProgressDirectory practice)
{
    public async Task<IReadOnlyList<TeacherGroupView>> ListGroupsAsync(Guid teacherUserId, CancellationToken cancellationToken)
    {
        var groups = await teaching.GetGroupsAsync(teacherUserId, cancellationToken);
        if (groups.Count == 0) return [];
        var active = await roster.CountActiveAsync(groups.Select(group => group.OfferingId).ToArray(), cancellationToken);
        return groups.Select(group => ToView(group, active.GetValueOrDefault(group.OfferingId))).ToArray();
    }

    /// <summary>Null when the teacher is not assigned to the group.</summary>
    public async Task<TeacherGroupDetail?> GetGroupAsync(Guid teacherUserId, Guid offeringId, CancellationToken cancellationToken)
    {
        // Courses DbContext: one after the other.
        var group = (await teaching.GetGroupsAsync(teacherUserId, cancellationToken)).FirstOrDefault(item => item.OfferingId == offeringId);
        if (group is null) return null;
        var sessions = await teaching.GetGroupSessionsAsync(teacherUserId, offeringId, cancellationToken) ?? [];

        // Enrollments and Attendance are different DbContexts: roster and marks per class at the same time.
        var rosterTask = roster.GetActiveStudentIdsAsync(offeringId, cancellationToken);
        var marksTask = records.CountBySessionsAsync(sessions.Select(session => session.SessionId).ToArray(), cancellationToken);
        await Task.WhenAll(rosterTask, marksTask);
        var students = rosterTask.Result;

        var perStudent = await records.CountByStudentInOfferingAsync(offeringId, cancellationToken);
        var peopleTask = users.GetManyAsync(students, cancellationToken);
        var practiceTask = practice.GetCourseProgressAsync(students, group.CourseId, cancellationToken);
        await Task.WhenAll(peopleTask, practiceTask);
        var people = peopleTask.Result.ToDictionary(person => person.Id);

        var counts = perStudent.ToLookup(count => count.StudentUserId);
        var studentViews = students.Where(people.ContainsKey).Select(student =>
            {
                var own = counts[student].ToDictionary(count => count.Status, count => count.Count);
                return new TeacherStudentView(student, people[student].FullName,
                    AttendanceSummary.From(own.GetValueOrDefault(Domain.AttendanceStatus.Present),
                        own.GetValueOrDefault(Domain.AttendanceStatus.Late), own.GetValueOrDefault(Domain.AttendanceStatus.Absent)),
                    practiceTask.Result.GetValueOrDefault(student) ?? Progress.Of(0, 0));
            })
            .OrderBy(student => student.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var sessionViews = sessions.Select((session, index) =>
        {
            var (present, late, absent) = marksTask.Result.GetValueOrDefault(session.SessionId);
            return new TeacherSessionView(session.SessionId, index + 1, session.StartsAt, session.EndsAt, session.VirtualLink,
                present, late, absent, present + late + absent);
        }).ToArray();

        return new TeacherGroupDetail(ToView(group, students.Count), sessionViews, studentViews);
    }

    private static TeacherGroupView ToView(TeacherGroup group, int activeStudents) =>
        new(group.OfferingId, group.CourseId, group.CourseTitle, group.OfferingName, group.IsPublished, activeStudents,
            group.MaximumCapacity, group.Sessions, group.NextSessionId, group.NextSessionStartsAt);
}
