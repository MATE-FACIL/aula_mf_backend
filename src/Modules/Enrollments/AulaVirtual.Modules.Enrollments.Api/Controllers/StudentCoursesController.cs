using Asp.Versioning;
using AulaVirtual.Modules.Enrollments.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Enrollments.Api.Controllers;

// The signed-in student's courses and their lessons. Everything starts from the caller's own active
// enrollments, so there is no student id to tamper with and any other account gets an empty list or 404.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/students/me/courses")]
[Authorize]
public sealed class StudentCoursesController(StudentLessonsUseCases useCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthorized();
        return Ok(await useCases.ListCoursesAsync(userId, cancellationToken));
    }

    [HttpGet("{courseId:guid}/lessons")]
    public async Task<IActionResult> Lessons(Guid courseId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthorized();
        var content = await useCases.GetLessonsAsync(userId, courseId, cancellationToken);
        return content is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found",
                detail: "No tienes una matrícula activa en ese curso.")
            : Ok(content);
    }

    private bool TryGetUser(out Guid userId) =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out userId) && userId != Guid.Empty;

    private new ObjectResult Unauthorized() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");
}
