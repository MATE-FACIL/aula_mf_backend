using Asp.Versioning;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Exams.Application.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Exams.Api.Controllers;

public sealed record ReplaceExercisesRequest(IReadOnlyList<ExerciseInput>? Exercises);

public sealed record AnswerRequest(string? Answer);

public abstract class PracticeControllerBase : ControllerBase
{
    protected bool TryGetUser(out Guid userId) =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out userId) && userId != Guid.Empty;

    protected ObjectResult Unauthenticated() => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");

    protected IActionResult ToResult<T>(PracticeResult<T> result) => result.Status switch
    {
        PracticeResultStatus.Ok => Ok(result.Value),
        PracticeResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
        PracticeResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
        PracticeResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid practice", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown practice result.")
    };
}

// The exercises of a lesson (SCRUM-59), managed by whoever manages courses. Answers included: team only.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/courses/{courseId:guid}/lessons/{lessonId:guid}/exercises")]
[Authorize(Policy = Permissions.CoursesManage)]
public sealed class LessonExercisesController(PracticeAdminUseCases useCases) : PracticeControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid courseId, Guid lessonId, CancellationToken cancellationToken) =>
        ToResult(await useCases.GetAsync(courseId, lessonId, cancellationToken));

    /// <summary>Replaces the ordered list; exercises left out are archived (students keep their history).</summary>
    [HttpPut]
    public async Task<IActionResult> Replace(Guid courseId, Guid lessonId, ReplaceExercisesRequest request,
        CancellationToken cancellationToken) =>
        ToResult(await useCases.ReplaceAsync(courseId, lessonId, request.Exercises, cancellationToken));
}

// The signed-in student's practice (SCRUM-59/60). Everything starts from the caller's own enrollments.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/students/me")]
[Authorize]
public sealed class StudentPracticeController(StudentPracticeUseCases useCases) : PracticeControllerBase
{
    [HttpGet("lessons/{lessonId:guid}/practice")]
    public async Task<IActionResult> Lesson(Guid lessonId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.GetLessonAsync(userId, lessonId, cancellationToken));
    }

    [HttpPost("exercises/{exerciseId:guid}/answers")]
    public async Task<IActionResult> Answer(Guid exerciseId, AnswerRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.AnswerAsync(userId, exerciseId, request.Answer, cancellationToken));
    }

    [HttpGet("courses/{courseId:guid}/progress")]
    public async Task<IActionResult> Progress(Guid courseId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.GetCourseProgressAsync(userId, courseId, cancellationToken));
    }

    [HttpGet("practice/attempts")]
    public async Task<IActionResult> Attempts([FromQuery] Guid? lessonId, CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.GetAttemptsAsync(userId, lessonId, page, pageSize, cancellationToken));
    }
}
