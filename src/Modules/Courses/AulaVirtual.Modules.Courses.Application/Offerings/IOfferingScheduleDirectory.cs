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

/// <summary>One class with what attendance needs: who teaches it and whether its group is published.</summary>
public sealed record SessionDetails(
    Guid SessionId,
    Guid OfferingId,
    string OfferingName,
    string CourseTitle,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsPublished,
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

    /// <summary>A single class, drafts included (the caller decides). Two queries: the class and its teachers.</summary>
    Task<SessionDetails?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}
