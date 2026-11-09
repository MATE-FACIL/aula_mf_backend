namespace AulaVirtual.SharedKernel.Application.Storage;

public static class FileObjectKeys
{
    public static string CourseCover(Guid courseId, Guid fileId) =>
        Join("courses", courseId, "covers", fileId);

    /// <summary>A lesson's video or downloadable material (SCRUM-58). Always in the private bucket.</summary>
    public static string LessonFile(Guid courseId, Guid lessonId, Guid fileId) =>
        Join("courses", courseId, "lessons", lessonId, "files", fileId);

    public static string SessionRecording(Guid editionId, Guid sessionId, Guid fileId) =>
        Join("course-offerings", editionId, "sessions", sessionId, "recordings", fileId);

    public static string Certificate(Guid certificateId, Guid fileId) =>
        Join("certificates", certificateId, fileId);

    public static void Validate(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var parts = objectKey.Split('/');
        if (parts.Length < 3 || parts.Any(p => p.Length == 0 || p == "." || p == "..") ||
            parts.Any(p => p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))))
            throw new ArgumentException("Object key contains invalid segments.", nameof(objectKey));

        static bool Id(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;
        var valid = parts switch
        {
            ["courses", var course, "covers", var file] => Id(course) && Id(file),
            ["courses", var course, "lessons", var lesson, "files", var file] => Id(course) && Id(lesson) && Id(file),
            ["course-offerings", var edition, "sessions", var session, "recordings", var file] =>
                Id(edition) && Id(session) && Id(file),
            ["certificates", var certificate, var file] => Id(certificate) && Id(file),
            _ => false
        };
        if (!valid) throw new ArgumentException("Object key is not a supported server-generated path.", nameof(objectKey));
    }

    private static string Join(params object[] segments)
    {
        if (segments.OfType<Guid>().Any(id => id == Guid.Empty))
            throw new ArgumentException("Object key identifiers must not be empty.");
        return string.Join('/', segments.Select(s => s is Guid id ? id.ToString("N") : s));
    }
}
