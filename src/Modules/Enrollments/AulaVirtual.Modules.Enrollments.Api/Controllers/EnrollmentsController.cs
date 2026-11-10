using Asp.Versioning;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Enrollments.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Enrollments.Api.Controllers;

public sealed record EnrollRequest(Guid StudentUserId, Guid CourseOfferingId, string? PaymentNote);

public sealed record ActivateEnrollmentRequest(string? PaymentNote);

public sealed record TransferEnrollmentRequest(Guid CourseOfferingId, string? Reason);

[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/enrollments")]
[Authorize(Policy = Permissions.EnrollmentsManage)]
public sealed class EnrollmentsController(
    EnrollmentUseCases useCases,
    EnrollmentTransferUseCases transferUseCases,
    PlacementUseCases placementUseCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? courseOfferingId,
        [FromQuery] Guid? studentUserId,
        [FromQuery] string? status,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = EnrollmentListQuery.DefaultPageSize) =>
        ToActionResult(await useCases.ListAsync(
            new EnrollmentListQuery(courseOfferingId, studentUserId, status, page, pageSize), cancellationToken), Ok);

    /// <summary>Editions with capacity and seats taken (pending + active).</summary>
    [HttpGet("offerings")]
    public async Task<IActionResult> Offerings(CancellationToken cancellationToken) =>
        Ok(await useCases.ListOfferingSeatsAsync(cancellationToken));

    /// <summary>
    /// Where to place a student (SCRUM-45): the published Mathematics groups with their free seats, those of the level the
    /// teacher confirmed in the diagnostic first, and the student's current seats.
    /// </summary>
    [HttpGet("placement")]
    public async Task<IActionResult> Placement([FromQuery] Guid studentUserId, CancellationToken cancellationToken) =>
        ToActionResult(await placementUseCases.GetAsync(studentUserId, cancellationToken), Ok);

    /// <summary>The changes of group, newest first. <c>courseOfferingId</c> matches the group a student left or joined.</summary>
    [HttpGet("transfers")]
    public async Task<IActionResult> Transfers(
        [FromQuery] Guid? studentUserId,
        [FromQuery] Guid? courseOfferingId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = EnrollmentListQuery.DefaultPageSize) =>
        Ok(await transferUseCases.ListAsync(new TransferListQuery(studentUserId, courseOfferingId, page, pageSize), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Enroll(EnrollRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actor)) return Unauthorized();
        return ToActionResult(await useCases.EnrollAsync(actor, request.StudentUserId, request.CourseOfferingId,
            request.PaymentNote, cancellationToken), view => StatusCode(StatusCodes.Status201Created, view));
    }

    [HttpPost("{enrollmentId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid enrollmentId, ActivateEnrollmentRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actor)) return Unauthorized();
        return ToActionResult(await useCases.ActivateAsync(actor, enrollmentId, request?.PaymentNote,
            cancellationToken), Ok);
    }

    /// <summary>
    /// Moves the student to another group: the destination needs a free seat; the old seat is withdrawn, the new one keeps
    /// the status and the change is recorded with who, when and why.
    /// </summary>
    [HttpPost("{enrollmentId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid enrollmentId, TransferEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actor)) return Unauthorized();
        return ToActionResult(await transferUseCases.TransferAsync(actor, enrollmentId, request.CourseOfferingId, request.Reason,
            cancellationToken), Ok);
    }

    [HttpPost("{enrollmentId:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid enrollmentId, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actor)) return Unauthorized();
        return ToActionResult(await useCases.WithdrawAsync(actor, enrollmentId, cancellationToken), Ok);
    }

    private bool TryGetActor(out Guid actorUserId) =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out actorUserId) && actorUserId != Guid.Empty;

    private new ObjectResult Unauthorized() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");

    private IActionResult ToActionResult<T>(EnrollmentResult<T> result, Func<T, IActionResult> onOk) =>
        result.Status switch
        {
            EnrollmentResultStatus.Ok => onOk(result.Value!),
            EnrollmentResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Not found", detail: result.Detail),
            EnrollmentResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Conflict", detail: result.Detail),
            EnrollmentResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request", detail: result.Detail),
            _ => throw new InvalidOperationException("Unknown enrollment result.")
        };
}
