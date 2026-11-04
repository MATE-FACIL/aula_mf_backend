using Asp.Versioning;
using AulaVirtual.Modules.Enrollments.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Enrollments.Api.Controllers;

// The signed-in guardian's children. No role query and no student id in the route: the list starts from
// the caller's own links, so any other account simply gets an empty list.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/families/me/children")]
[Authorize]
public sealed class FamilyController(FamilyUseCases useCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("user_id")?.Value, out var userId) || userId == Guid.Empty)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");
        }

        return Ok(await useCases.ListChildrenAsync(userId, cancellationToken));
    }
}
