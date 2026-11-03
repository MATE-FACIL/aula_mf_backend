using Asp.Versioning;
using AulaVirtual.Modules.Administration.Api.Contracts;
using AulaVirtual.Modules.Administration.Application.Users;
using AulaVirtual.Modules.Authorization.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Administration.Api.Controllers;

public sealed record CreateInstructorRequest(
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string Email,
    string? DocumentType,
    string? DocumentNumber)
{
    public CreateInstructorInput ToInput() =>
        new(GivenNames, FirstSurname, SecondSurname, Email, DocumentType, DocumentNumber);
}

public sealed record CreatedInstructorResponse(Guid InstructorId, Guid UserId, string LoginIdentifier,
    string InitialPassword);

[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/admin/instructors")]
[Authorize(Policy = Permissions.InstructorsManage)]
public sealed class AdminInstructorsController(AdminInstructorUseCases useCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await useCases.ListAsync(cancellationToken));

    // The response carries a one-time password: it must never be stored by a cache.
    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Create(CreateInstructorRequest request, CancellationToken cancellationToken)
    {
        var result = await useCases.CreateAsync(request.ToInput(), cancellationToken);
        return result.Status switch
        {
            AdminResultStatus.Ok => StatusCode(StatusCodes.Status201Created, new CreatedInstructorResponse(
                result.Value!.InstructorId, result.Value.Credentials.UserId,
                result.Value.Credentials.LoginIdentifier, result.Value.Credentials.InitialPassword)),
            AdminResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                detail: result.Detail),
            AdminResultStatus.Invalid when result.Errors is { Count: > 0 } => ValidationProblem(
                new ValidationProblemDetails(result.Errors) { Detail = result.Detail }),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: result.Detail)
        };
    }
}
