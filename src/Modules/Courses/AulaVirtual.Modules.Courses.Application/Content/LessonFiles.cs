using AulaVirtual.Modules.Courses.Domain.Content;
using AulaVirtual.SharedKernel.Application.Storage;

namespace AulaVirtual.Modules.Courses.Application.Content;

// Lesson files in private storage (SCRUM-58). Uploads go straight from the browser to storage with a short-lived
// grant, so large videos never pass through the API. Downloads are signed URLs that expire, handed out only
// after the caller has been authorized (team for the admin view, active enrollment for students).
//
// Signing is a local computation (no network call), so signing every file of a course stays cheap.
public sealed class LessonFiles(IFileStorage? storage)
{
    public static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DownloadWindow = TimeSpan.FromMinutes(15);

    public bool Available => storage is not null;

    public static string Key(Guid courseId, Guid lessonId, Guid fileId) => FileObjectKeys.LessonFile(courseId, lessonId, fileId);

    public async Task<UploadGrant> GrantUploadAsync(Guid courseId, Guid lessonId, LessonFileKind kind, string contentType,
        long size, CancellationToken cancellationToken)
    {
        var fileId = Guid.NewGuid();
        var grant = await Require().CreateDirectUploadAsync(new DirectUploadRequest(Key(courseId, lessonId, fileId),
            FileVisibility.Private, contentType.Trim(), size, UploadWindow), cancellationToken);
        return new UploadGrant(fileId, grant.Url, grant.Method, grant.Headers, grant.ExpiresAt,
            kind == LessonFileKind.Video ? LessonFileRules.MaxVideoBytes : LessonFileRules.MaxMaterialBytes);
    }

    /// <summary>What storage holds for an uploaded file, or null when nothing was uploaded under that id.</summary>
    public Task<FileObjectInfo?> FindAsync(Guid courseId, Guid lessonId, Guid fileId, CancellationToken cancellationToken) =>
        Require().GetInfoAsync(Key(courseId, lessonId, fileId), FileVisibility.Private, cancellationToken);

    /// <summary>Best effort: a file left behind costs storage, never correctness.</summary>
    public async Task<int> DeleteAsync(Guid courseId, Guid lessonId, IEnumerable<Guid> fileIds, CancellationToken cancellationToken)
    {
        if (storage is null) return 0;
        var failed = 0;
        foreach (var fileId in fileIds)
        {
            try
            {
                await storage.DeleteAsync(Key(courseId, lessonId, fileId), FileVisibility.Private, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
            }
        }

        return failed;
    }

    /// <summary>Replaces each uploaded file with a signed download URL (null when storage is unavailable).</summary>
    public async Task<CourseContentView> SignAsync(CourseContentView content, CancellationToken cancellationToken)
    {
        if (!content.Units.SelectMany(unit => unit.Lessons).Any(HasFiles)) return content;

        var units = new List<UnitView>(content.Units.Count);
        foreach (var unit in content.Units)
        {
            var lessons = new List<LessonView>(unit.Lessons.Count);
            foreach (var lesson in unit.Lessons)
            {
                if (!HasFiles(lesson))
                {
                    lessons.Add(lesson);
                    continue;
                }

                var video = lesson.VideoFileId is { } videoId
                    ? await SignOneAsync(content.CourseId, lesson.Id, videoId, cancellationToken)
                    : lesson.VideoUrl;
                var materials = new List<MaterialView>(lesson.Materials.Count);
                foreach (var material in lesson.Materials)
                    materials.Add(material.FileId is { } fileId
                        ? material with { Url = await SignOneAsync(content.CourseId, lesson.Id, fileId, cancellationToken) }
                        : material);
                lessons.Add(lesson with { VideoUrl = video, Materials = materials });
            }

            units.Add(unit with { Lessons = lessons });
        }

        return content with { Units = units };
    }

    private static bool HasFiles(LessonView lesson) =>
        lesson.VideoFileId is not null || lesson.Materials.Any(material => material.FileId is not null);

    private async Task<string?> SignOneAsync(Guid courseId, Guid lessonId, Guid fileId, CancellationToken cancellationToken) =>
        storage is null
            ? null
            : (await storage.GetPrivateDownloadUrlAsync(Key(courseId, lessonId, fileId), DownloadWindow, cancellationToken)).AbsoluteUri;

    private IFileStorage Require() =>
        storage ?? throw new InvalidOperationException("File storage is not configured.");
}

/// <summary>Students' read of a course, with signed links for uploaded files.</summary>
public sealed class SignedCourseContentDirectory(ICourseContentRepository repository, LessonFiles files) : ICourseContentDirectory
{
    public async Task<CourseContentView?> GetPublishedContentAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var content = await repository.GetPublishedContentAsync(courseId, cancellationToken);
        return content is null ? null : await files.SignAsync(content, cancellationToken);
    }
}
