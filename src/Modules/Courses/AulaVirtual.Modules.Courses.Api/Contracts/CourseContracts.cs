using AulaVirtual.Modules.Courses.Application.Courses;
using AulaVirtual.SharedKernel.Application.Storage;

namespace AulaVirtual.Modules.Courses.Api.Contracts;

public sealed record SaveCourseRequest(string Title, Guid? CategoryId, string? ShortDescription,
    string? FullDescription, string? Objectives, string? Syllabus,
    string? TargetAudience, string? Prerequisites, int? SessionDurationMinutes = null)
{
    public SaveCourseCommand ToCommand() => new(CategoryId, Title, ShortDescription,
        FullDescription, Objectives, Syllabus, TargetAudience, Prerequisites, SessionDurationMinutes);
}

public sealed record SetCoursePublicationRequest(bool IsPublished);

public sealed record CourseAdminResponse(Guid Id, string Title, Guid? CategoryId, bool IsPublished,
    string? ShortDescription, string? FullDescription, string? Objectives, string? Syllabus,
    string? TargetAudience, string? Prerequisites, string? CoverObjectKey,
    string? CoverContentType, long? CoverSize, string? CoverOriginalFileName, string? CoverUrl,
    int? SessionDurationMinutes)
{
    public static CourseAdminResponse From(CourseDetails details, IFileStorage? storage) => new(details.Id, details.Title,
        details.CategoryId, details.IsPublished, details.ShortDescription, details.FullDescription,
        details.Objectives, details.Syllabus, details.TargetAudience, details.Prerequisites,
        details.CoverObjectKey, details.CoverContentType, details.CoverSize, details.CoverOriginalFileName,
        CourseCoverUrl.From(details.CoverObjectKey, storage), details.SessionDurationMinutes);
}

public sealed record CourseCategoryResponse(Guid Id, string Name)
{
    public static CourseCategoryResponse From(CourseCategoryDetails details) => new(details.Id, details.Name);
}

public sealed record SaveCourseCategoryRequest(string Name);
