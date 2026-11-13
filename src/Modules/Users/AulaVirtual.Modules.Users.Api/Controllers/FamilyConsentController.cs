using Asp.Versioning;
using AulaVirtual.Modules.Users.Application.Consent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Users.Api.Controllers;

public sealed record AcceptConsentRequest(string? Version, IReadOnlyList<Guid>? StudentUserIds);

// The signed-in guardian's consent for their children's data (SCRUM-38). No role query and no guardian id in the
// route: everything starts from the caller's own links, so any other account gets an empty list or a 404.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/families/me/consent")]
[Authorize]
public sealed class FamilyConsentController(GuardianConsentUseCases useCases) : ControllerBase
{
    /// <summary>The current text (terms and privacy) and, for each child, whether a guardian already accepted it.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        return Ok(await useCases.GetAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Accepts the current text for these children (all or none). <c>version</c> must be the one that was read: if the
    /// text changed in the meantime, 409.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Accept(AcceptConsentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId)) return Unauthenticated();
        var result = await useCases.AcceptAsync(userId, request.Version, request.StudentUserIds, cancellationToken);
        return result.Status switch
        {
            ConsentOutcomeStatus.Ok => Ok(result.Value),
            ConsentOutcomeStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
            ConsentOutcomeStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
            ConsentOutcomeStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid consent", detail: result.Detail),
            _ => throw new InvalidOperationException("Unknown consent result.")
        };
    }

    private bool TryGetUser(out Guid userId) =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out userId) && userId != Guid.Empty;

    private ObjectResult Unauthenticated() => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized.");
}
