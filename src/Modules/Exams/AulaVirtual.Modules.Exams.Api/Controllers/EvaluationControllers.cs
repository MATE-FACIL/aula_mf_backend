using Asp.Versioning;
using AulaVirtual.Modules.Exams.Application.Evaluations;
using AulaVirtual.Modules.Exams.Domain.Evaluations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Exams.Api.Controllers;

public sealed record SubmitEvaluationRequest(Dictionary<string, string?>? Answers);

public abstract class EvaluationControllerBase : ControllerBase
{
    protected bool TryGetUser(out Guid userId) =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out userId) && userId != Guid.Empty;

    protected ObjectResult Unauthenticated() => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");

    protected IActionResult ToResult<T>(EvaluationOutcome<T> result, int successStatus = StatusCodes.Status200OK) => result.Status switch
    {
        EvaluationResultStatus.Ok => successStatus == StatusCodes.Status200OK ? Ok(result.Value) : StatusCode(successStatus, result.Value),
        EvaluationResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
        EvaluationResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
        EvaluationResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid evaluation", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown evaluation result.")
    };
}

// The teacher's evaluations (SCRUM-62): only the groups the signed-in teacher is assigned to. Any other group or
// evaluation is a 404, so a teacher cannot tell whether it exists.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/teachers/me")]
[Authorize]
public sealed class TeacherEvaluationsController(TeacherEvaluationUseCases useCases) : EvaluationControllerBase
{
    /// <summary>The competencies a question can measure (generic for now).</summary>
    [HttpGet("competencies")]
    public IActionResult Competencies() =>
        Ok(Domain.Evaluations.Competencies.All.Select(item => new { code = item.Key, label = item.Value }));

    [HttpGet("groups/{offeringId:guid}/evaluations")]
    public async Task<IActionResult> List(Guid offeringId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.ListAsync(userId, offeringId, cancellationToken));
    }

    [HttpPost("groups/{offeringId:guid}/evaluations")]
    public async Task<IActionResult> Create(Guid offeringId, EvaluationInput input, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.CreateAsync(userId, offeringId, input, cancellationToken), StatusCodes.Status201Created);
    }

    [HttpGet("evaluations/{evaluationId:guid}")]
    public async Task<IActionResult> Get(Guid evaluationId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.GetAsync(userId, evaluationId, cancellationToken));
    }

    [HttpPut("evaluations/{evaluationId:guid}")]
    public async Task<IActionResult> Update(Guid evaluationId, EvaluationInput input, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.UpdateAsync(userId, evaluationId, input, cancellationToken));
    }

    [HttpDelete("evaluations/{evaluationId:guid}")]
    public async Task<IActionResult> Delete(Guid evaluationId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        var result = await useCases.DeleteAsync(userId, evaluationId, cancellationToken);
        return result.Status == EvaluationResultStatus.Ok ? NoContent() : ToResult(result);
    }

    [HttpGet("evaluations/{evaluationId:guid}/results")]
    public async Task<IActionResult> Results(Guid evaluationId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.ResultsAsync(userId, evaluationId, cancellationToken));
    }
}

// The student's evaluations (SCRUM-62): everything starts from the caller's own active enrollments, and time is decided
// by the server.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/students/me/evaluations")]
[Authorize]
public sealed class StudentEvaluationsController(StudentEvaluationUseCases useCases) : EvaluationControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.ListAsync(userId, cancellationToken));
    }

    [HttpPost("{evaluationId:guid}/attempts")]
    public async Task<IActionResult> Start(Guid evaluationId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.StartAsync(userId, evaluationId, cancellationToken), StatusCodes.Status201Created);
    }

    [HttpPost("attempts/{attemptId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid attemptId, SubmitEvaluationRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.SubmitAsync(userId, attemptId, request.Answers, cancellationToken));
    }

    [HttpGet("{evaluationId:guid}/result")]
    public async Task<IActionResult> Result(Guid evaluationId, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return ToResult(await useCases.ResultAsync(userId, evaluationId, cancellationToken));
    }
}
