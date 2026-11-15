using Asp.Versioning;
using AulaVirtual.Modules.Attendance.Application;
using AulaVirtual.Modules.Exams.Application.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Attendance.Api.Controllers;

public sealed record MarkAttendanceRequest(IReadOnlyList<AttendanceEntry>? Records);

public abstract class SignedInController : ControllerBase
{
    protected bool TryGetUser(out Guid userId) =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out userId) && userId != Guid.Empty;

    protected ObjectResult Unauthenticated() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");
}

// Taking attendance for a class (SCRUM-53). The access rule (team permission or teacher of the group)
// depends on the class, so it is checked by the use case rather than by a fixed policy.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/attendance/sessions/{sessionId:guid}")]
[Authorize]
public sealed class SessionAttendanceController(AttendanceUseCases useCases) : SignedInController
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.GetSessionAsync(userId, sessionId, cancellationToken));
    }

    /// <summary>Saves the whole roster at once: one request and one transaction for the class.</summary>
    [HttpPut]
    public async Task<IActionResult> Mark(Guid sessionId, MarkAttendanceRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.MarkAsync(userId, sessionId, request.Records, cancellationToken));
    }

    /// <summary>«Marcar asistencia en 1 clic»: every active student without a mark becomes present; existing marks stay.</summary>
    [HttpPost("present-all")]
    public async Task<IActionResult> MarkRemainingPresent(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.MarkRemainingPresentAsync(userId, sessionId, cancellationToken));
    }

    private IActionResult ToResult(AttendanceResult<SessionAttendanceView> result) => result.Status switch
    {
        AttendanceResultStatus.Ok => Ok(result.Value),
        AttendanceResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
        AttendanceResultStatus.Forbidden => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: result.Detail),
        AttendanceResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
        AttendanceResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid attendance", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown attendance result.")
    };
}

// The signed-in student's own attendance. Any other account simply gets empty totals.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/students/me/attendance")]
[Authorize]
public sealed class StudentAttendanceController(StudentAttendanceUseCases useCases) : SignedInController
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.ForStudentAsync(userId, cancellationToken));
    }
}

// The signed-in guardian's children's attendance, starting from their own links (SCRUM-35, SCRUM-53).
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/families/me/attendance")]
[Authorize]
public sealed class FamilyAttendanceController(StudentAttendanceUseCases useCases) : SignedInController
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.ForGuardianAsync(userId, cancellationToken));
    }
}

// The teacher panel (SCRUM-69): the signed-in teacher's own groups only. Any other account gets an empty list or 404.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/teachers/me/groups")]
[Authorize]
public sealed class TeacherPanelController(TeacherPanelUseCases useCases) : SignedInController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.ListGroupsAsync(userId, cancellationToken));
    }

    [HttpGet("{offeringId:guid}")]
    public async Task<IActionResult> Get(Guid offeringId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        var group = await useCases.GetGroupAsync(userId, offeringId, cancellationToken);
        return group is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No tienes ese grupo asignado.")
            : Ok(group);
    }
}

public sealed record ReplaceTeacherExercisesRequest(IReadOnlyList<ExerciseInput>? Exercises);

// The teacher edits the practice exercises of the courses they teach (a course counts when they are assigned to one of
// its groups). Answers included: the teacher is part of the team for their own courses. Any other course is a 404.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/teachers/me/courses/{courseId:guid}")]
[Authorize]
public sealed class TeacherPracticeController(TeacherPracticeUseCases useCases) : SignedInController
{
    [HttpGet("lessons")]
    public async Task<IActionResult> Lessons(Guid courseId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        var lessons = await useCases.GetLessonsAsync(userId, courseId, cancellationToken);
        return lessons is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No tienes asignado ese curso.")
            : Ok(lessons);
    }

    [HttpGet("lessons/{lessonId:guid}/exercises")]
    public async Task<IActionResult> Get(Guid courseId, Guid lessonId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.GetExercisesAsync(userId, courseId, lessonId, cancellationToken));
    }

    [HttpPut("lessons/{lessonId:guid}/exercises")]
    public async Task<IActionResult> Replace(Guid courseId, Guid lessonId, ReplaceTeacherExercisesRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.ReplaceExercisesAsync(userId, courseId, lessonId, request.Exercises, cancellationToken));
    }

    private IActionResult ToResult<T>(PracticeResult<T> result) => result.Status switch
    {
        PracticeResultStatus.Ok => Ok(result.Value),
        PracticeResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
        PracticeResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
        PracticeResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid practice", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown practice result.")
    };
}
