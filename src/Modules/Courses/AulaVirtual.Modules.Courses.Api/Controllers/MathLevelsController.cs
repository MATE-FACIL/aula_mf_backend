using Asp.Versioning;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Levels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Courses.Api.Controllers;

// The Mathematics levels (SCRUM-39), managed by whoever manages courses.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/math-levels")]
[Authorize(Policy = Permissions.CoursesManage)]
public sealed class MathLevelsController(MathLevelUseCases useCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await useCases.ListAsync(cancellationToken));

    /// <summary>Creates the missing levels (draft courses with their 14 draft sessions). Safe to repeat.</summary>
    [HttpPost("provision")]
    public async Task<IActionResult> Provision(CancellationToken cancellationToken)
    {
        var result = await useCases.ProvisionAsync(cancellationToken);
        return result is null
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                detail: "Otra persona estaba creando los niveles al mismo tiempo. Recarga la página.")
            : Ok(result);
    }
}
