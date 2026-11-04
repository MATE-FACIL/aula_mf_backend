using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Enrollments.Application;

public sealed record FamilyEnrollment(
    Guid Id,
    Guid CourseOfferingId,
    string CourseTitle,
    string OfferingName,
    string Status,
    DateTimeOffset? ActivatedAt);

public sealed record FamilyChild(
    Guid StudentUserId,
    string GivenNames,
    string FullName,
    IReadOnlyList<FamilyEnrollment> Enrollments,
    IReadOnlyList<StudentClass> UpcomingClasses);

// Family panel (SCRUM-67). Takes only the signed-in guardian's id and starts from their own links, so a
// guardian cannot ask for another student: there is no student id to tamper with (SCRUM-35).
//
// Six queries in total, whatever the number of children: links → seats → editions → sessions + teachers →
// people (children and teachers in a single batch). Everything is loaded across children, never per child.
public sealed class FamilyUseCases(
    IGuardianLinks links,
    IEnrollmentRepository enrollments,
    IOfferingDirectory offerings,
    IOfferingScheduleDirectory schedule,
    IUserDirectory users,
    TimeProvider timeProvider)
{
    public const int UpcomingClassesPerChild = 5;
    private const int MaxSessions = 200;
    private static readonly TimeSpan UpcomingWindow = TimeSpan.FromDays(21);

    public async Task<IReadOnlyList<FamilyChild>> ListChildrenAsync(Guid guardianUserId,
        CancellationToken cancellationToken)
    {
        var studentIds = await links.GetStudentIdsAsync(guardianUserId, cancellationToken);
        if (studentIds.Count == 0) return [];

        var seats = await enrollments.GetLiveByStudentsAsync(studentIds, cancellationToken);
        var offeringIds = seats.Select(seat => seat.CourseOfferingId).ToHashSet();
        var activeOfferingIds = seats.Where(seat => seat.Status == EnrollmentStatus.Active)
            .Select(seat => seat.CourseOfferingId).ToHashSet();

        // Editions and sessions share the Courses DbContext: one after the other, never in parallel.
        var editions = (await offerings.GetManyAsync(offeringIds, cancellationToken))
            .ToDictionary(edition => edition.Id);
        var now = timeProvider.GetUtcNow();
        var sessions = await schedule.GetSessionsAsync(activeOfferingIds, now, now + UpcomingWindow, MaxSessions,
            cancellationToken);

        var peopleIds = studentIds.Concat(sessions.SelectMany(session => session.InstructorUserIds)).ToHashSet();
        var people = (await users.GetManyAsync(peopleIds, cancellationToken)).ToDictionary(person => person.Id);

        var seatsByStudent = seats.ToLookup(seat => seat.StudentUserId);
        return studentIds
            .Where(people.ContainsKey)
            .Select(studentId =>
            {
                var own = seatsByStudent[studentId];
                var active = own.Where(seat => seat.Status == EnrollmentStatus.Active)
                    .Select(seat => seat.CourseOfferingId).ToHashSet();

                return new FamilyChild(
                    studentId,
                    people[studentId].GivenNames,
                    people[studentId].FullName,
                    own.Select(seat => ToEnrollment(seat, editions.GetValueOrDefault(seat.CourseOfferingId))).ToArray(),
                    sessions.Where(session => active.Contains(session.OfferingId))
                        .Take(UpcomingClassesPerChild)
                        .Select(session => ToClass(session, people))
                        .ToArray());
            })
            .OrderBy(child => child.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static FamilyEnrollment ToEnrollment(Enrollment seat, OfferingSummary? edition) => new(
        seat.Id,
        seat.CourseOfferingId,
        edition?.CourseTitle ?? "(curso eliminado)",
        edition?.Name ?? "(edición eliminada)",
        EnrollmentStatusCodes.From(seat.Status),
        seat.ActivatedAt);

    private static StudentClass ToClass(ScheduledSession session, IReadOnlyDictionary<Guid, UserSummary> people) => new(
        session.SessionId,
        session.OfferingId,
        session.CourseTitle,
        session.OfferingName,
        session.CategoryName,
        session.StartsAt,
        session.EndsAt,
        session.VirtualLink,
        session.InstructorUserIds
            .Select(id => people.TryGetValue(id, out var teacher) ? $"{teacher.GivenNames} {teacher.FirstSurname}" : null)
            .OfType<string>()
            .ToArray());
}
