using Asp.Versioning;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Exams.Application.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Exams.Api.Controllers;

public sealed record ReplaceDiagnosticQuestionsRequest(IReadOnlyList<DiagnosticQuestionInput>? Questions);

public sealed record StartDiagnosticRequest(string? Stage);

/// <param name="Skip">True for «No lo sé»: the question counts as wrong and <paramref name="Answer"/> is ignored.</param>
public sealed record DiagnosticAnswerRequest(Guid QuestionId, string? Answer, bool Skip);

public sealed record ConfirmDiagnosticRequest(string? Level);

public abstract class DiagnosticControllerBase : PracticeControllerBase
{
    protected IActionResult ToResult<T>(DiagnosticOutcome<T> result) => result.Status switch
    {
        DiagnosticResultStatus.Ok => Ok(result.Value),
        DiagnosticResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
        DiagnosticResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
        DiagnosticResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid diagnostic", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown diagnostic result.")
    };
}

// The question bank of the entry diagnostic (SCRUM-47), managed by whoever manages courses. Answers included.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/diagnostics/questions/{stage}")]
[Authorize(Policy = Permissions.CoursesManage)]
public sealed class DiagnosticQuestionsController(DiagnosticBankUseCases useCases) : DiagnosticControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string stage, CancellationToken cancellationToken) =>
        ToResult(await useCases.GetAsync(stage, cancellationToken));

    /// <summary>Replaces the stage's ordered list; questions left out are archived (started tests keep them).</summary>
    [HttpPut]
    public async Task<IActionResult> Replace(string stage, ReplaceDiagnosticQuestionsRequest request, CancellationToken cancellationToken) =>
        ToResult(await useCases.ReplaceAsync(stage, request.Questions, cancellationToken));
}

// The signed-in student's diagnostics (SCRUM-48/49). Everything starts from the caller's own attempts.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/students/me/diagnostics")]
[Authorize]
public sealed class StudentDiagnosticsController(StudentDiagnosticUseCases useCases) : DiagnosticControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.OverviewAsync(userId, cancellationToken));
    }

    /// <summary>Starts the stage's diagnostic, or returns the one under way.</summary>
    [HttpPost]
    public async Task<IActionResult> Start(StartDiagnosticRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.StartAsync(userId, request.Stage, cancellationToken));
    }

    [HttpGet("{attemptId:guid}")]
    public async Task<IActionResult> Get(Guid attemptId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.GetAsync(userId, attemptId, cancellationToken));
    }

    [HttpPost("{attemptId:guid}/answers")]
    public async Task<IActionResult> Answer(Guid attemptId, DiagnosticAnswerRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.AnswerAsync(userId, attemptId, request.QuestionId, request.Answer, request.Skip, cancellationToken));
    }
}

// The teacher's review (SCRUM-48): list, detail and confirmation of the level.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/diagnostics")]
[Authorize(Policy = Permissions.DiagnosticsReview)]
public sealed class DiagnosticReviewController(DiagnosticReviewUseCases useCases) : DiagnosticControllerBase
{
    /// <param name="status"><c>pending</c> (default), <c>confirmed</c>, <c>in_progress</c> or <c>all</c>.</param>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] string? status = "pending",
        [FromQuery] string? stage = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var filter = status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "pending" => DiagnosticFilter.Pending,
            "confirmed" => DiagnosticFilter.Confirmed,
            "in_progress" => DiagnosticFilter.InProgress,
            "all" => DiagnosticFilter.All,
            _ => (DiagnosticFilter?)null
        };
        if (filter is null)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid diagnostic",
                detail: "El estado es «pending», «confirmed», «in_progress» o «all».");
        return Ok(await useCases.ListAsync(filter.Value, stage, page, pageSize, cancellationToken));
    }

    [HttpGet("{attemptId:guid}")]
    public async Task<IActionResult> Get(Guid attemptId, CancellationToken cancellationToken) =>
        ToResult(await useCases.GetAsync(attemptId, cancellationToken));

    [HttpPost("{attemptId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid attemptId, ConfirmDiagnosticRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.ConfirmAsync(attemptId, request.Level, userId, cancellationToken));
    }
}
