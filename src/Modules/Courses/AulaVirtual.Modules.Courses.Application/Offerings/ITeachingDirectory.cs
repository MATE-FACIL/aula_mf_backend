namespace AulaVirtual.Modules.Courses.Application.Offerings;

/// <summary>A group a teacher is assigned to, with its next class (null when none is left).</summary>
public sealed record TeacherGroup(
    Guid OfferingId,
    Guid CourseId,
    string CourseTitle,
    string OfferingName,
    bool IsPublished,
    int MaximumCapacity,
    int Sessions,
    Guid? NextSessionId,
    DateTimeOffset? NextSessionStartsAt);

public sealed record GroupSession(Guid SessionId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? VirtualLink);

// Cross-module read contract for the teacher panel (SCRUM-69). Everything starts from the teacher's user id, so a
// teacher only ever sees the groups they are assigned to.
public interface ITeachingDirectory
{
    /// <summary>The teacher's groups (drafts included), newest course first. Two queries.</summary>
    Task<IReadOnlyList<TeacherGroup>> GetGroupsAsync(Guid teacherUserId, CancellationToken cancellationToken);

    /// <summary>Null when the teacher is not assigned to the group; otherwise its classes in order. Two queries.</summary>
    Task<IReadOnlyList<GroupSession>?> GetGroupSessionsAsync(Guid teacherUserId, Guid offeringId,
        CancellationToken cancellationToken);
}
