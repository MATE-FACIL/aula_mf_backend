using AulaVirtual.Modules.Courses.Domain.Courses;
using AulaVirtual.SharedKernel.Application.Storage;

namespace AulaVirtual.Modules.Courses.Application.Courses;

public sealed record SaveCourseCommand(Guid? CategoryId, string Title, string? ShortDescription,
    string? FullDescription, string? Objectives, string? Syllabus,
    string? TargetAudience, string? Prerequisites, int? SessionDurationMinutes = null);

public sealed record CourseDetails(Guid Id, string Title, Guid? CategoryId, bool IsPublished,
    string? ShortDescription, string? FullDescription, string? Objectives, string? Syllabus,
    string? TargetAudience, string? Prerequisites, string? CoverObjectKey,
    string? CoverContentType, long? CoverSize, string? CoverOriginalFileName, int? SessionDurationMinutes = null)
{
    public static CourseDetails From(Course course) => new(course.Id, course.Title, course.CategoryId,
        course.IsPublished, course.ShortDescription, course.FullDescription, course.Objectives,
        course.Syllabus, course.TargetAudience, course.Prerequisites, course.CoverObjectKey,
        course.CoverContentType, course.CoverSize, course.CoverOriginalFileName, course.SessionDurationMinutes);
}

public static class CourseCoverUrl
{
    public static string? From(string? objectKey, IFileStorage? storage) =>
        objectKey is null || storage is null ? null : storage.GetPublicUrl(objectKey)?.AbsoluteUri;
}

public sealed record CourseCategoryDetails(Guid Id, string Name)
{
    public static CourseCategoryDetails From(CourseCategory category) => new(category.Id, category.Name);
}
