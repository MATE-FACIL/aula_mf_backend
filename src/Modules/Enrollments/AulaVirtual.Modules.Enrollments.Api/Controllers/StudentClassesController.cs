using Asp.Versioning;
using AulaVirtual.Modules.Enrollments.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Enrollments.Api.Controllers;

// The signed-in user's own classes. No extra role query: the data comes from the caller's own active
// enrollments, so an account that is not an enrolled student simply gets an empty list.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/students/me/classes")]
[Authorize]
public sealed class StudentClassesController(StudentScheduleUseCases useCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("user_id")?.Value, out var userId) || userId == Guid.Empty)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");
        }

        var classes = await useCases.ListClassesAsync(userId, from?.ToUniversalTime(), to?.ToUniversalTime(),
            cancellationToken);
        return classes is null
            ? Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid range",
                detail: $"El rango debe ser válido y de máximo {StudentScheduleUseCases.MaxRange.TotalDays} días.")
            : Ok(classes);
    }
}
