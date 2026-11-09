namespace AulaVirtual.Modules.Courses.Domain.Content;

/// <summary>A file already uploaded to private storage for this lesson (SCRUM-58).</summary>
public sealed record StoredFile(Guid FileId, string ContentType, long Size);

/// <summary>
/// What an administrator writes for a lesson; materials keep the given order. The video is either an external
/// link (<paramref name="VideoUrl"/>, e.g. YouTube) or an uploaded file (<paramref name="VideoFile"/>), never both.
/// </summary>
public sealed record LessonContent(
    string Title,
    string? Summary,
    string? VideoUrl,
    bool IsPublished,
    IReadOnlyList<MaterialContent> Materials,
    StoredFile? VideoFile = null);

/// <summary>A material is either an HTTPS link or an uploaded file.</summary>
public sealed record MaterialContent(string Title, string? Url, StoredFile? File = null);

// One lesson of a course (SCRUM-55): video, files and, from SCRUM-59, the practice that points to it.
// Lessons are ordered inside their unit; the course order (1…N, «las 14 sesiones») is unit order then
// lesson order. Unpublished lessons are drafts: students never see them.
public sealed class Lesson
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 2000;
    public const int MaxMaterials = 20;

    private readonly List<LessonMaterial> materials = [];

    private Lesson()
    {
    }

    private Lesson(Guid id, Guid courseId, Guid unitId, int position, DateTimeOffset createdAt)
    {
        Id = id;
        CourseId = courseId;
        UnitId = unitId;
        Position = position;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid UnitId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }
    public string? VideoUrl { get; private set; }
    public Guid? VideoFileId { get; private set; }
    public string? VideoContentType { get; private set; }
    public long? VideoSize { get; private set; }
    public int Position { get; private set; }
    public bool IsPublished { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public IReadOnlyList<LessonMaterial> Materials => materials;

    /// <summary>Every uploaded file the lesson points to (video and materials).</summary>
    public IReadOnlyDictionary<Guid, StoredFile> Files()
    {
        var files = materials.Where(item => item.FileId is not null)
            .ToDictionary(item => item.FileId!.Value, item => new StoredFile(item.FileId!.Value, item.ContentType!, item.Size!.Value));
        if (VideoFileId is { } videoId) files[videoId] = new StoredFile(videoId, VideoContentType!, VideoSize!.Value);
        return files;
    }

    public static Lesson Create(Guid id, Guid courseId, Guid unitId, int position, LessonContent content,
        DateTimeOffset createdAt)
    {
        ContentRules.RequireId(id, nameof(id));
        ContentRules.RequireId(courseId, nameof(courseId));
        ContentRules.RequireId(unitId, nameof(unitId));
        var lesson = new Lesson(id, courseId, unitId, ContentRules.Position(position), createdAt);
        lesson.Apply(content);
        return lesson;
    }

    /// <summary>Replaces texts, video and the whole list of materials.</summary>
    public void Update(LessonContent content, DateTimeOffset now)
    {
        Apply(content);
        UpdatedAt = now;
    }

    public void MoveTo(Guid unitId, int position, DateTimeOffset now)
    {
        ContentRules.RequireId(unitId, nameof(unitId));
        UnitId = unitId;
        Position = ContentRules.Position(position);
        UpdatedAt = now;
    }

    private void Apply(LessonContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var items = content.Materials ?? [];
        if (items.Count > MaxMaterials)
            throw new ArgumentException($"Una lección admite como máximo {MaxMaterials} materiales.", nameof(content));
        if (content.VideoFile is not null && !string.IsNullOrWhiteSpace(content.VideoUrl))
            throw new ArgumentException("El video es un enlace o un archivo subido, no ambos.", nameof(content));

        // Validate everything before changing anything: a bad material leaves the lesson as it was.
        var title = ContentRules.Title(content.Title, MaxTitleLength, nameof(content.Title));
        var summary = ContentRules.OptionalText(content.Summary, MaxSummaryLength, "El resumen", nameof(content.Summary));
        var video = ContentRules.OptionalHttpsUrl(content.VideoUrl, "El video", nameof(content.VideoUrl));
        var videoFile = content.VideoFile is null ? null : ContentRules.File(content.VideoFile, nameof(content.VideoFile));
        var newMaterials = items.Select((item, index) => LessonMaterial.Create(Guid.NewGuid(), Id, item, index + 1)).ToArray();

        Title = title;
        Summary = summary;
        VideoUrl = video;
        VideoFileId = videoFile?.FileId;
        VideoContentType = videoFile?.ContentType;
        VideoSize = videoFile?.Size;
        IsPublished = content.IsPublished;
        materials.Clear();
        materials.AddRange(newMaterials);
    }
}

// A downloadable file or external resource of a lesson (PDF, worksheet, slides…): an HTTPS link or a file
// uploaded to private storage (SCRUM-58), which only enrolled students can download.
public sealed class LessonMaterial
{
    public const int MaxTitleLength = 150;

    private LessonMaterial()
    {
    }

    private LessonMaterial(Guid id, Guid lessonId, string title, string? url, StoredFile? file, int position)
    {
        Id = id;
        LessonId = lessonId;
        Title = title;
        Url = url;
        FileId = file?.FileId;
        ContentType = file?.ContentType;
        Size = file?.Size;
        Position = position;
    }

    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Url { get; private set; }
    public Guid? FileId { get; private set; }
    public string? ContentType { get; private set; }
    public long? Size { get; private set; }
    public int Position { get; private set; }

    internal static LessonMaterial Create(Guid id, Guid lessonId, MaterialContent content, int position)
    {
        ArgumentNullException.ThrowIfNull(content);
        var title = ContentRules.Title(content.Title, MaxTitleLength, nameof(content.Title));
        var hasUrl = !string.IsNullOrWhiteSpace(content.Url);
        if (hasUrl == (content.File is not null))
            throw new ArgumentException($"El material «{title}» debe ser un enlace o un archivo subido.", nameof(content));
        return new LessonMaterial(id, lessonId, title,
            hasUrl ? ContentRules.HttpsUrl(content.Url, "El material", nameof(content.Url)) : null,
            content.File is null ? null : ContentRules.File(content.File, nameof(content.File)),
            ContentRules.Position(position));
    }
}
