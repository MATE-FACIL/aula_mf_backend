using Asp.Versioning;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AulaVirtual.Modules.Courses.Api.Controllers;

public sealed record SaveUnitRequest(string Title);

public sealed record ReorderRequest(IReadOnlyList<Guid> Ids);

/// <summary>A material is a link (<paramref name="Url"/>) or a file uploaded for this lesson (<paramref name="FileId"/>).</summary>
public sealed record MaterialRequest(string Title, string? Url, Guid? FileId = null);

public sealed record SaveLessonRequest(
    string Title,
    string? Summary,
    string? VideoUrl,
    bool IsPublished,
    IReadOnlyList<MaterialRequest>? Materials,
    // Only on update: a different unit moves the lesson to the end of that unit.
    Guid? UnitId = null,
    // Only on update: a video uploaded for this lesson, instead of VideoUrl.
    Guid? VideoFileId = null)
{
    public LessonInput ToInput() => new(Title, Summary, VideoUrl, VideoFileId, IsPublished,
        (Materials ?? []).Select(item => new MaterialInput(item.Title, item.Url, item.FileId)).ToArray());
}

/// <summary><paramref name="Kind"/>: «video» or «material».</summary>
public sealed record UploadRequest(string Kind, string ContentType, long Size);

// Units and lessons of a course (SCRUM-55), managed by whoever manages courses.
[ApiController]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/courses/{courseId:guid}/content")]
[Authorize(Policy = Permissions.CoursesManage)]
public sealed class CourseContentController(CourseContentUseCases useCases) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid courseId, CancellationToken cancellationToken) =>
        ToResult(await useCases.GetContentAsync(courseId, cancellationToken), Ok);

    [HttpPost("units")]
    public async Task<IActionResult> AddUnit(Guid courseId, SaveUnitRequest request, CancellationToken cancellationToken) =>
        ToResult(await useCases.AddUnitAsync(courseId, request.Title, cancellationToken), Created);

    [HttpPut("units/order")]
    public async Task<IActionResult> ReorderUnits(Guid courseId, ReorderRequest request, CancellationToken cancellationToken) =>
        ToResult(await useCases.ReorderUnitsAsync(courseId, request.Ids, cancellationToken), Ok);

    [HttpPut("units/{unitId:guid}")]
    public async Task<IActionResult> RenameUnit(Guid courseId, Guid unitId, SaveUnitRequest request,
        CancellationToken cancellationToken) =>
        ToResult(await useCases.RenameUnitAsync(courseId, unitId, request.Title, cancellationToken), Ok);

    [HttpDelete("units/{unitId:guid}")]
    public async Task<IActionResult> DeleteUnit(Guid courseId, Guid unitId, CancellationToken cancellationToken) =>
        ToResult(await useCases.DeleteUnitAsync(courseId, unitId, cancellationToken), _ => NoContent());

    [HttpPost("units/{unitId:guid}/lessons")]
    public async Task<IActionResult> AddLesson(Guid courseId, Guid unitId, SaveLessonRequest request,
        CancellationToken cancellationToken) =>
        ToResult(await useCases.AddLessonAsync(courseId, unitId, request.ToInput(), cancellationToken), Created);

    [HttpPut("units/{unitId:guid}/lessons/order")]
    public async Task<IActionResult> ReorderLessons(Guid courseId, Guid unitId, ReorderRequest request,
        CancellationToken cancellationToken) =>
        ToResult(await useCases.ReorderLessonsAsync(courseId, unitId, request.Ids, cancellationToken), Ok);

    [HttpPut("lessons/{lessonId:guid}")]
    public async Task<IActionResult> UpdateLesson(Guid courseId, Guid lessonId, SaveLessonRequest request,
        CancellationToken cancellationToken)
    {
        if (request.UnitId is not { } unitId || unitId == Guid.Empty)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid lesson", detail: "Indica la unidad de la lección.");
        return ToResult(await useCases.UpdateLessonAsync(courseId, lessonId, unitId, request.ToInput(), cancellationToken), Ok);
    }

    /// <summary>
    /// Grants a direct upload to private storage (SCRUM-58). The client PUTs the file to the returned URL with the
    /// returned headers, then attaches it by sending its fileId in the next lesson update.
    /// </summary>
    [HttpPost("lessons/{lessonId:guid}/uploads")]
    public async Task<IActionResult> GrantUpload(Guid courseId, Guid lessonId, UploadRequest request,
        CancellationToken cancellationToken) =>
        ToResult(await useCases.GrantUploadAsync(courseId, lessonId, request.Kind, request.ContentType, request.Size,
            cancellationToken), Created);

    [HttpDelete("lessons/{lessonId:guid}")]
    public async Task<IActionResult> DeleteLesson(Guid courseId, Guid lessonId, CancellationToken cancellationToken) =>
        ToResult(await useCases.DeleteLessonAsync(courseId, lessonId, cancellationToken), _ => NoContent());

    private ObjectResult Created<T>(T value) => StatusCode(StatusCodes.Status201Created, value);

    private IActionResult ToResult<T>(ContentResult<T> result, Func<T, IActionResult> onOk) => result.Status switch
    {
        ContentResultStatus.Ok => onOk(result.Value!),
        ContentResultStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Detail),
        ContentResultStatus.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.Detail),
        ContentResultStatus.Invalid => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid content", detail: result.Detail),
        ContentResultStatus.Unavailable => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage unavailable", detail: result.Detail),
        _ => throw new InvalidOperationException("Unknown content result.")
    };
}
