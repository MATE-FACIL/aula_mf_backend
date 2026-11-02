using Asp.Versioning;
using AulaVirtual.Modules.Administration.Api.Contracts;
using AulaVirtual.Modules.Administration.Application.Users;
using AulaVirtual.Modules.Authorization.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Administration.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/admin")]
[Authorize(Policy = Permissions.UsersManage)]
public sealed class AdminUsersController(AdminUserUseCases useCases) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Ok(await useCases.GetSummaryAsync(cancellationToken));

    [HttpGet("users")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] string? status,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminUserListQuery.DefaultPageSize) =>
        ToActionResult(await useCases.ListAsync(
            new AdminUserListQuery(search, role, status, page, pageSize), cancellationToken), page => Ok(page));

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        var user = await useCases.GetAsync(userId, cancellationToken);
        return user is null ? Missing("No encontramos a esa persona.") : Ok(user);
    }

    // The response carries a one-time password: it must never be stored by a cache.
    [HttpPost("students")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CreateStudent(CreateStudentRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await useCases.CreateStudentAsync(request.ToInput(), cancellationToken), CreatedUser);

    [HttpPost("guardians")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CreateGuardian(CreateGuardianRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await useCases.CreateGuardianAsync(request.ToInput(), cancellationToken), CreatedUser);

    [HttpPut("users/{userId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid userId, SetUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("user_id")?.Value, out var actorUserId))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");
        }

        return ToActionResult(
            await useCases.SetActiveAsync(actorUserId, userId, request.IsActive, cancellationToken),
            _ => NoContent());
    }

    [HttpPut("guardians/{guardianId:guid}/students/{studentId:guid}")]
    public async Task<IActionResult> Link(Guid guardianId, Guid studentId, CancellationToken cancellationToken) =>
        ToActionResult(await useCases.LinkAsync(guardianId, studentId, cancellationToken), _ => NoContent());

    // Idempotent: removing a link that does not exist is not an error.
    [HttpDelete("guardians/{guardianId:guid}/students/{studentId:guid}")]
    public async Task<IActionResult> Unlink(Guid guardianId, Guid studentId, CancellationToken cancellationToken)
    {
        await useCases.UnlinkAsync(guardianId, studentId, cancellationToken);
        return NoContent();
    }

    private IActionResult CreatedUser(IssuedCredentials credentials) =>
        CreatedAtAction(nameof(Get), new { version = "1", userId = credentials.UserId },
            IssuedCredentialsResponse.From(credentials));

    private IActionResult ToActionResult<T>(AdminResult<T> result, Func<T, IActionResult> onOk) => result.Status switch
    {
        AdminResultStatus.Ok => onOk(result.Value!),
        AdminResultStatus.NotFound => Missing(result.Detail),
        AdminResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Conflict", detail: result.Detail),
        AdminResultStatus.Invalid when result.Errors is { Count: > 0 } => ValidationProblem(
            new ValidationProblemDetails(result.Errors) { Detail = result.Detail }),
        AdminResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid request", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown administration result.")
    };

    private ObjectResult Missing(string? detail) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: detail);
}
