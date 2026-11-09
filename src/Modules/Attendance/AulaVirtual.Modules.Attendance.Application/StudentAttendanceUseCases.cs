using AulaVirtual.Modules.Attendance.Domain;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Attendance.Application;

// Attendance as the student and the family see it (SCRUM-53: «visible para la familia»). Both start from the
// signed-in account (the student itself, or the guardian's own links): no student id comes from the request.
//
// Fixed cost, whatever the number of children or classes: links (1, family only), counts (1), recent rows (1),
// group names (1). The totals cover every class; the list shows the most recent ones.
public sealed class StudentAttendanceUseCases(
    IAttendanceRepository records,
    IGuardianLinks links,
    IOfferingDirectory offerings,
    TimeProvider timeProvider)
{
    public const int RecentForStudent = 30;
    public const int RecentPerChild = 10;
    private const int MaxRecentRows = 500;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(120);

    public async Task<StudentAttendanceReport> ForStudentAsync(Guid studentUserId, CancellationToken cancellationToken) =>
        (await BuildAsync([studentUserId], RecentForStudent, cancellationToken))[0];

    public async Task<IReadOnlyList<StudentAttendanceReport>> ForGuardianAsync(Guid guardianUserId,
        CancellationToken cancellationToken)
    {
        var children = await links.GetStudentIdsAsync(guardianUserId, cancellationToken);
        return children.Count == 0 ? [] : await BuildAsync(children, RecentPerChild, cancellationToken);
    }

    private async Task<IReadOnlyList<StudentAttendanceReport>> BuildAsync(IReadOnlyList<Guid> studentIds, int recentPerStudent,
        CancellationToken cancellationToken)
    {
        // Same DbContext: one after the other.
        var counts = await records.CountByStatusAsync(studentIds, cancellationToken);
        // A fixed cap over a fixed window, never «N × children»: a child with many classes cannot crowd out
        // the others' history, and the query stays bounded.
        var recent = await records.GetRecentAsync(studentIds, timeProvider.GetUtcNow() - RecentWindow, MaxRecentRows,
            cancellationToken);

        var editions = recent.Count == 0
            ? new Dictionary<Guid, OfferingSummary>()
            : (await offerings.GetManyAsync(recent.Select(record => record.CourseOfferingId).ToHashSet(), cancellationToken))
                .ToDictionary(edition => edition.Id);

        var countsByStudent = counts.ToLookup(count => count.StudentUserId);
        var recentByStudent = recent.ToLookup(record => record.StudentUserId);
        return studentIds.Select(studentId =>
        {
            var own = countsByStudent[studentId].ToDictionary(count => count.Status, count => count.Count);
            var summary = AttendanceSummary.From(own.GetValueOrDefault(AttendanceStatus.Present),
                own.GetValueOrDefault(AttendanceStatus.Late), own.GetValueOrDefault(AttendanceStatus.Absent));
            var items = recentByStudent[studentId]
                .Take(recentPerStudent)
                .Select(record =>
                {
                    var edition = editions.GetValueOrDefault(record.CourseOfferingId);
                    return new AttendanceItem(record.SessionId, edition?.CourseTitle ?? string.Empty, edition?.Name ?? string.Empty,
                        record.SessionStartsAt, AttendanceStatusCodes.ToCode(record.Status), record.Note);
                })
                .ToArray();
            return new StudentAttendanceReport(studentId, summary, items);
        }).ToArray();
    }
}
