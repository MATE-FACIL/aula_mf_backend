namespace AulaVirtual.Modules.Courses.Application.Offerings;

public sealed record ScheduledSession(
    Guid SessionId,
    Guid OfferingId,
    string OfferingName,
    string CourseTitle,
    string? CategoryName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? VirtualLink,
    IReadOnlyList<Guid> InstructorUserIds);

// Cross-module read contract for calendars (student and family). Only PUBLISHED editions are visible:
// drafts can still change their schedule.
public interface IOfferingScheduleDirectory
{
    /// <summary>
    /// Sessions of the given editions that overlap [from, to), ordered by start, at most <paramref name="limit"/>.
    /// Two queries in total: sessions (with edition, course and category) and the editions' teachers.
    /// </summary>
    Task<IReadOnlyList<ScheduledSession>> GetSessionsAsync(
        IReadOnlyCollection<Guid> offeringIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken);
}
