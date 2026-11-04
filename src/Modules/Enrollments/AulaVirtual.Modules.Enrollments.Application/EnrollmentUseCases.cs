using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Enrollments.Application;

// Manual enrollment (SCRUM-44): an administrator registers the student in an edition (pending),
// confirms the payment and activates it, or withdraws it. Seats are capped by the edition's
// MaximumCapacity (SCRUM-40); pending enrollments already hold a seat.
//
// Lists are paged in the database and enriched in batches (people from Users, editions from
// Courses): one page costs a fixed number of queries.
public sealed class EnrollmentUseCases(
    IEnrollmentRepository enrollments,
    IOfferingDirectory offerings,
    IUserDirectory users,
    IRoleDirectory roles,
    TimeProvider timeProvider)
{
    public async Task<EnrollmentResult<EnrollmentView>> EnrollAsync(
        Guid actorUserId,
        Guid studentUserId,
        Guid courseOfferingId,
        string? paymentNote,
        CancellationToken cancellationToken)
    {
        if (paymentNote?.Trim().Length > Enrollment.MaxPaymentNoteLength)
        {
            return EnrollmentResult<EnrollmentView>.Invalid("La nota de pago es demasiado larga (máximo 300 caracteres).");
        }

        var rolesTask = roles.GetRoleCodesByUserIdsAsync([studentUserId], cancellationToken);
        var offeringTask = offerings.GetAsync(courseOfferingId, cancellationToken);
        await Task.WhenAll(rolesTask, offeringTask);

        if (!(rolesTask.Result.GetValueOrDefault(studentUserId)?.Contains(Roles.Student) ?? false))
        {
            return EnrollmentResult<EnrollmentView>.Invalid("Solo se puede matricular a personas con rol de alumno.");
        }

        if (offeringTask.Result is not { } offering)
        {
            return EnrollmentResult<EnrollmentView>.NotFound("No encontramos esa edición del curso.");
        }

        // A draft can still change its schedule and teachers: nobody takes a seat until it is published.
        if (!offering.IsPublished)
        {
            return EnrollmentResult<EnrollmentView>.Conflict(
                "Este grupo todavía está en borrador. Publícalo para poder matricular.");
        }

        var enrollment = Enrollment.Create(Guid.NewGuid(), studentUserId, offering.Id, paymentNote, actorUserId,
            timeProvider.GetUtcNow());

        return await enrollments.AddWithinCapacityAsync(enrollment, offering.MaximumCapacity, cancellationToken) switch
        {
            SeatReservation.Added => EnrollmentResult<EnrollmentView>.Ok((await EnrichAsync([enrollment], cancellationToken))[0]),
            SeatReservation.AlreadyEnrolled => EnrollmentResult<EnrollmentView>.Conflict(
                "El alumno ya está matriculado en esta edición."),
            SeatReservation.Full => EnrollmentResult<EnrollmentView>.Conflict(
                $"El grupo está lleno ({offering.MaximumCapacity} de {offering.MaximumCapacity} cupos)."),
            _ => throw new InvalidOperationException("Unknown seat reservation result.")
        };
    }

    public Task<EnrollmentResult<EnrollmentView>> ActivateAsync(Guid actorUserId, Guid enrollmentId,
        string? paymentNote, CancellationToken cancellationToken) =>
        ChangeAsync(enrollmentId, enrollment =>
        {
            if (enrollment.Status != EnrollmentStatus.Pending)
                return "Solo se puede activar una matrícula pendiente.";
            if (paymentNote?.Trim().Length > Enrollment.MaxPaymentNoteLength)
                return "La nota de pago es demasiado larga (máximo 300 caracteres).";
            enrollment.Activate(actorUserId, paymentNote, timeProvider.GetUtcNow());
            return null;
        }, cancellationToken);

    public Task<EnrollmentResult<EnrollmentView>> WithdrawAsync(Guid actorUserId, Guid enrollmentId,
        CancellationToken cancellationToken) =>
        ChangeAsync(enrollmentId, enrollment =>
        {
            if (enrollment.Status == EnrollmentStatus.Withdrawn)
                return "La matrícula ya estaba retirada.";
            enrollment.Withdraw(actorUserId, timeProvider.GetUtcNow());
            return null;
        }, cancellationToken);

    public async Task<EnrollmentResult<EnrollmentPage>> ListAsync(EnrollmentListQuery query,
        CancellationToken cancellationToken)
    {
        if (!EnrollmentStatusCodes.TryParse(query.Status, out var status))
        {
            return EnrollmentResult<EnrollmentPage>.Invalid(
                $"Estado desconocido: «{query.Status}». Usa «pending», «active» o «withdrawn».");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, EnrollmentListQuery.MaxPageSize);
        var (items, total) = await enrollments.SearchAsync(
            new EnrollmentQuery(query.CourseOfferingId, query.StudentUserId, status, page, pageSize),
            cancellationToken);

        return EnrollmentResult<EnrollmentPage>.Ok(
            new EnrollmentPage(await EnrichAsync(items, cancellationToken), page, pageSize, total));
    }

    public async Task<IReadOnlyList<OfferingSeats>> ListOfferingSeatsAsync(CancellationToken cancellationToken)
    {
        // Different modules (Courses and Enrollments): both queries run at the same time.
        var listTask = offerings.ListAsync(cancellationToken);
        var seatsTask = enrollments.CountSeatsAsync(null, cancellationToken);
        await Task.WhenAll(listTask, seatsTask);

        return listTask.Result.Select(offering => new OfferingSeats(offering.Id, offering.CourseId,
                offering.CourseTitle, offering.Name, offering.IsPublished, offering.MaximumCapacity,
                seatsTask.Result.GetValueOrDefault(offering.Id)))
            .ToArray();
    }

    private async Task<EnrollmentResult<EnrollmentView>> ChangeAsync(Guid enrollmentId,
        Func<Enrollment, string?> change, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetAsync(enrollmentId, cancellationToken);
        if (enrollment is null) return EnrollmentResult<EnrollmentView>.NotFound("No encontramos esa matrícula.");

        var problem = change(enrollment);
        if (problem is not null) return EnrollmentResult<EnrollmentView>.Invalid(problem);

        await enrollments.SaveChangesAsync(cancellationToken);
        return EnrollmentResult<EnrollmentView>.Ok((await EnrichAsync([enrollment], cancellationToken))[0]);
    }

    private async Task<IReadOnlyList<EnrollmentView>> EnrichAsync(IReadOnlyList<Enrollment> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return [];

        var peopleIds = items
            .SelectMany(item => new[] { item.StudentUserId, item.CreatedByUserId, item.ActivatedByUserId, item.WithdrawnByUserId })
            .OfType<Guid>()
            .ToHashSet();
        var peopleTask = users.GetManyAsync(peopleIds, cancellationToken);
        var offeringsTask = offerings.GetManyAsync(items.Select(item => item.CourseOfferingId).ToHashSet(), cancellationToken);
        await Task.WhenAll(peopleTask, offeringsTask);

        var people = peopleTask.Result.ToDictionary(person => person.Id);
        var editions = offeringsTask.Result.ToDictionary(offering => offering.Id);
        string? NameOf(Guid? id) => id is { } value && people.TryGetValue(value, out var person) ? person.FullName : null;

        return items.Select(item =>
        {
            var student = people.GetValueOrDefault(item.StudentUserId);
            var edition = editions.GetValueOrDefault(item.CourseOfferingId);
            return new EnrollmentView(item.Id, item.StudentUserId, student?.FullName ?? "(persona eliminada)",
                student?.DocumentNumber, item.CourseOfferingId, edition?.CourseTitle ?? "(curso eliminado)",
                edition?.Name ?? "(edición eliminada)", EnrollmentStatusCodes.From(item.Status), item.PaymentNote,
                item.CreatedAt, NameOf(item.CreatedByUserId), item.ActivatedAt, NameOf(item.ActivatedByUserId),
                item.WithdrawnAt, NameOf(item.WithdrawnByUserId));
        }).ToArray();
    }
}
