using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Enrollments.Application;

public sealed record StudentClass(
    Guid SessionId,
    Guid OfferingId,
    string CourseTitle,
    string OfferingName,
    string? CategoryName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? VirtualLink,
    IReadOnlyList<string> Teachers);

// A student's live classes (SCRUM-50/51/52): sessions of the published editions where the student has an
// ACTIVE enrollment (a pending one is still waiting for payment). Fixed cost per request, whatever the
// number of classes: enrollments (1 query) → sessions + teachers (2) → teacher names (1).
// Written for any student id so the family panel (SCRUM-67) can reuse it for linked children.
public sealed class StudentScheduleUseCases(
    IEnrollmentRepository enrollments,
    IOfferingScheduleDirectory schedule,
    IUserDirectory users,
    TimeProvider timeProvider)
{
    public const int MaxClasses = 200;
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(120);
    private static readonly TimeSpan DefaultPast = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultFuture = TimeSpan.FromDays(35);

    public async Task<IReadOnlyList<StudentClass>?> ListClassesAsync(
        Guid studentUserId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var start = from ?? now - DefaultPast;
        var end = to ?? now + DefaultFuture;
        if (start >= end || end - start > MaxRange) return null;

        var offeringIds = await enrollments.GetActiveOfferingIdsAsync(studentUserId, cancellationToken);
        if (offeringIds.Count == 0) return [];

        var sessions = await schedule.GetSessionsAsync(offeringIds, start, end, MaxClasses, cancellationToken);
        if (sessions.Count == 0) return [];

        var teacherIds = sessions.SelectMany(session => session.InstructorUserIds).ToHashSet();
        var names = (await users.GetManyAsync(teacherIds, cancellationToken))
            .ToDictionary(person => person.Id, person => $"{person.GivenNames} {person.FirstSurname}");

        return sessions.Select(session => new StudentClass(session.SessionId, session.OfferingId, session.CourseTitle,
                session.OfferingName, session.CategoryName, session.StartsAt, session.EndsAt, session.VirtualLink,
                session.InstructorUserIds.Select(id => names.GetValueOrDefault(id)).OfType<string>().ToArray()))
            .ToArray();
    }
}
