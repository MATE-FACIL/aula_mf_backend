using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Users.Application.Consent;

public static class ConsentStatuses
{
    public const string Accepted = "accepted";
    public const string Pending = "pending";
}

/// <param name="Status"><c>accepted</c> (a linked guardian accepted the current text) or <c>pending</c>.</param>
public sealed record ChildConsent(Guid StudentUserId, string FullName, string Status, DateTimeOffset? AcceptedAt, string? AcceptedByName);

public sealed record FamilyConsent(ConsentText Text, IReadOnlyList<ChildConsent> Children, int Pending);

public enum ConsentOutcomeStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

public sealed record ConsentOutcome<T>(ConsentOutcomeStatus Status, T? Value = default, string? Detail = null)
{
    public static ConsentOutcome<T> Ok(T value) => new(ConsentOutcomeStatus.Ok, value);

    public static ConsentOutcome<T> NotFound(string detail) => new(ConsentOutcomeStatus.NotFound, Detail: detail);

    public static ConsentOutcome<T> Invalid(string detail) => new(ConsentOutcomeStatus.Invalid, Detail: detail);

    public static ConsentOutcome<T> Conflict(string detail) => new(ConsentOutcomeStatus.Conflict, Detail: detail);
}

// The parent's consent for their children's data (SCRUM-38). Takes only the signed-in guardian's id and starts from
// their own links, so there is no student id to tamper with: any other account gets an empty list. One acceptance
// is enough for a child (either parent); it counts while that guardian is still linked and until the text changes.
// The consent is recorded, it does not block anything yet: whether activating a seat requires it is up to the owner.
public sealed class GuardianConsentUseCases(
    IGuardianLinks links,
    IGuardianConsents consents,
    IUserDirectory users,
    TimeProvider timeProvider)
{
    public const int MaxChildrenPerRequest = 20;

    /// <summary>The current text and where each child stands. Three queries: links → acceptances → people.</summary>
    public async Task<FamilyConsent> GetAsync(Guid guardianUserId, CancellationToken cancellationToken)
    {
        var text = ConsentTexts.Current;
        var studentIds = await links.GetStudentIdsAsync(guardianUserId, cancellationToken);
        if (studentIds.Count == 0) return new FamilyConsent(text, [], 0);

        var accepted = await consents.GetAcceptedAsync(studentIds, text.Version, cancellationToken);
        var peopleIds = studentIds.Concat(accepted.Select(record => record.GuardianUserId)).ToHashSet();
        var people = (await users.GetManyAsync(peopleIds, cancellationToken)).ToDictionary(person => person.Id);

        var children = studentIds
            .Where(people.ContainsKey)
            .Select(studentId =>
            {
                var latest = accepted.Where(record => record.StudentUserId == studentId)
                    .OrderByDescending(record => record.AcceptedAt).FirstOrDefault();
                return latest is null
                    ? new ChildConsent(studentId, people[studentId].FullName, ConsentStatuses.Pending, null, null)
                    : new ChildConsent(studentId, people[studentId].FullName, ConsentStatuses.Accepted, latest.AcceptedAt,
                        people.TryGetValue(latest.GuardianUserId, out var by) ? by.FullName : null);
            })
            .OrderBy(child => child.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new FamilyConsent(text, children, children.Count(child => child.Status == ConsentStatuses.Pending));
    }

    /// <summary>
    /// Accepts the current text for these children, all or none: if one is not the guardian's, nothing is saved.
    /// The version must be the current one, so nobody accepts a text they did not read.
    /// </summary>
    public async Task<ConsentOutcome<FamilyConsent>> AcceptAsync(Guid guardianUserId, string? version,
        IReadOnlyList<Guid>? studentUserIds, CancellationToken cancellationToken)
    {
        if (version != ConsentTexts.Current.Version)
        {
            return ConsentOutcome<FamilyConsent>.Conflict(
                "El texto de los términos cambió mientras lo leías. Recarga la página para leer la versión actual.");
        }

        var ids = (studentUserIds ?? []).Distinct().ToArray();
        if (ids.Length == 0) return ConsentOutcome<FamilyConsent>.Invalid("Elige al menos un hijo.");
        if (ids.Length > MaxChildrenPerRequest)
        {
            return ConsentOutcome<FamilyConsent>.Invalid($"Se puede aceptar para como máximo {MaxChildrenPerRequest} hijos a la vez.");
        }

        var own = (await links.GetStudentIdsAsync(guardianUserId, cancellationToken)).ToHashSet();
        if (ids.Any(id => !own.Contains(id)))
        {
            return ConsentOutcome<FamilyConsent>.NotFound("No encontramos a uno de los hijos en tu cuenta.");
        }

        await consents.AcceptAsync(guardianUserId, ids, version, timeProvider.GetUtcNow(), cancellationToken);
        return ConsentOutcome<FamilyConsent>.Ok(await GetAsync(guardianUserId, cancellationToken));
    }
}
