using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Enrollments.Application;

public enum TransferOutcome
{
    Transferred,
    SourceNotFound,
    // The seat was already withdrawn (maybe by someone else a moment ago).
    SourceWithdrawn,
    SameGroup,
    AlreadyEnrolled,
    Full
}

/// <summary>The result of a move; the seats and the record are only set when <see cref="Outcome"/> is Transferred.</summary>
public sealed record TransferAttempt(TransferOutcome Outcome, Enrollment? NewEnrollment = null, EnrollmentTransfer? Transfer = null);

public interface IEnrollmentTransferRepository
{
    /// <summary>
    /// Moves a seat to another group in ONE transaction: the destination capacity is checked, the old seat is
    /// withdrawn, the new one is created and the change is recorded, or nothing happens. Moves of the same student
    /// are serialized and so are moves and enrollments into the same group, so concurrent requests cannot exceed the
    /// capacity nor leave the student with two seats.
    /// </summary>
    Task<TransferAttempt> TransferAsync(Guid enrollmentId, Guid destinationOfferingId, int destinationCapacity,
        Guid actorUserId, string? reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Newest first, paged in the database. <paramref name="courseOfferingId"/> matches either side of the move.</summary>
    Task<(IReadOnlyList<EnrollmentTransfer> Items, int Total)> SearchTransfersAsync(Guid? studentUserId, Guid? courseOfferingId,
        int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record TransferListQuery(Guid? StudentUserId, Guid? CourseOfferingId, int Page = 1,
    int PageSize = EnrollmentListQuery.DefaultPageSize);

public sealed record EnrollmentTransferView(
    Guid Id,
    Guid StudentUserId,
    string StudentName,
    Guid FromOfferingId,
    string FromCourseTitle,
    string FromOfferingName,
    Guid ToOfferingId,
    string ToCourseTitle,
    string ToOfferingName,
    Guid FromEnrollmentId,
    Guid ToEnrollmentId,
    string? Reason,
    DateTimeOffset TransferredAt,
    string? TransferredByName);

public sealed record EnrollmentTransferPage(IReadOnlyList<EnrollmentTransferView> Items, int Page, int PageSize, int Total);

// Change of group (SCRUM-45): an administrator moves a student to another group when the diagnostic or the family
// calls for it. The destination must be published and have a free seat; the change is recorded with who did it, when
// and why. Lists are paged in the database and enriched in batches: a fixed number of queries per page.
public sealed class EnrollmentTransferUseCases(
    IEnrollmentTransferRepository transfers,
    IOfferingDirectory offerings,
    IUserDirectory users,
    TimeProvider timeProvider)
{
    /// <summary>Destination (1) + the move (1 transaction) + names (2, in parallel).</summary>
    public async Task<EnrollmentResult<EnrollmentTransferView>> TransferAsync(Guid actorUserId, Guid enrollmentId,
        Guid destinationOfferingId, string? reason, CancellationToken cancellationToken)
    {
        if (reason?.Trim().Length > EnrollmentTransfer.MaxReasonLength)
        {
            return EnrollmentResult<EnrollmentTransferView>.Invalid(
                $"El motivo es demasiado largo (máximo {EnrollmentTransfer.MaxReasonLength} caracteres).");
        }

        if (await offerings.GetAsync(destinationOfferingId, cancellationToken) is not { } destination)
        {
            return EnrollmentResult<EnrollmentTransferView>.NotFound("No encontramos el grupo de destino.");
        }

        // A draft can still change its schedule and teachers: nobody takes a seat until it is published.
        if (!destination.IsPublished)
        {
            return EnrollmentResult<EnrollmentTransferView>.Conflict(
                "El grupo de destino todavía está en borrador. Publícalo para poder pasar alumnos a él.");
        }

        var attempt = await transfers.TransferAsync(enrollmentId, destination.Id, destination.MaximumCapacity, actorUserId,
            reason, timeProvider.GetUtcNow(), cancellationToken);
        switch (attempt.Outcome)
        {
            case TransferOutcome.SourceNotFound:
                return EnrollmentResult<EnrollmentTransferView>.NotFound("No encontramos esa matrícula.");
            case TransferOutcome.SourceWithdrawn:
                return EnrollmentResult<EnrollmentTransferView>.Conflict("La matrícula ya estaba retirada: no se puede pasar a otro grupo.");
            case TransferOutcome.SameGroup:
                return EnrollmentResult<EnrollmentTransferView>.Invalid("El alumno ya está en ese grupo.");
            case TransferOutcome.AlreadyEnrolled:
                return EnrollmentResult<EnrollmentTransferView>.Conflict("El alumno ya tiene una matrícula en el grupo de destino.");
            case TransferOutcome.Full:
                return EnrollmentResult<EnrollmentTransferView>.Conflict(
                    $"El grupo de destino está lleno ({destination.MaximumCapacity} de {destination.MaximumCapacity} cupos).");
            case TransferOutcome.Transferred:
                return EnrollmentResult<EnrollmentTransferView>.Ok((await EnrichAsync([attempt.Transfer!], cancellationToken))[0]);
            default:
                throw new InvalidOperationException("Unknown transfer outcome.");
        }
    }

    public async Task<EnrollmentTransferPage> ListAsync(TransferListQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, EnrollmentListQuery.MaxPageSize);
        var (items, total) = await transfers.SearchTransfersAsync(query.StudentUserId, query.CourseOfferingId, page, pageSize,
            cancellationToken);
        return new EnrollmentTransferPage(await EnrichAsync(items, cancellationToken), page, pageSize, total);
    }

    private async Task<IReadOnlyList<EnrollmentTransferView>> EnrichAsync(IReadOnlyList<EnrollmentTransfer> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return [];

        var peopleIds = items.SelectMany(item => new[] { item.StudentUserId, item.TransferredByUserId }).ToHashSet();
        var offeringIds = items.SelectMany(item => new[] { item.FromOfferingId, item.ToOfferingId }).ToHashSet();
        // Different modules (Users and Courses): both queries run at the same time.
        var peopleTask = users.GetManyAsync(peopleIds, cancellationToken);
        var offeringsTask = offerings.GetManyAsync(offeringIds, cancellationToken);
        await Task.WhenAll(peopleTask, offeringsTask);

        var people = peopleTask.Result.ToDictionary(person => person.Id);
        var editions = offeringsTask.Result.ToDictionary(offering => offering.Id);
        string NameOf(Guid id) => people.TryGetValue(id, out var person) ? person.FullName : "(persona eliminada)";
        (string Course, string Name) EditionOf(Guid id) => editions.TryGetValue(id, out var edition)
            ? (edition.CourseTitle, edition.Name)
            : ("(curso eliminado)", "(edición eliminada)");

        return items.Select(item =>
        {
            var from = EditionOf(item.FromOfferingId);
            var to = EditionOf(item.ToOfferingId);
            return new EnrollmentTransferView(item.Id, item.StudentUserId, NameOf(item.StudentUserId), item.FromOfferingId,
                from.Course, from.Name, item.ToOfferingId, to.Course, to.Name, item.FromEnrollmentId, item.ToEnrollmentId, item.Reason,
                item.TransferredAt, people.ContainsKey(item.TransferredByUserId) ? NameOf(item.TransferredByUserId) : null);
        }).ToArray();
    }
}
