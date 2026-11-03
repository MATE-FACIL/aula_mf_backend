using Asp.Versioning;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Enrollments.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Enrollments.Api.Controllers;

public sealed record EnrollRequest(Guid StudentUserId, Guid CourseOfferingId, string? PaymentNote);

public sealed record ActivateEnrollmentRequest(string? PaymentNote);

[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/enrollments")]
[Authorize(Policy = Permissions.EnrollmentsManage)]
public sealed class EnrollmentsController(EnrollmentUseCases useCases) : ControllerBase
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
