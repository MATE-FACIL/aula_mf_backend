using AulaVirtual.Modules.Courses.Domain.Content;
using Microsoft.Extensions.Logging;

namespace AulaVirtual.Modules.Courses.Application.Content;

// Administration of a course's units and lessons (SCRUM-55) and of their files (SCRUM-58). Content is written
// once and read many times, so every change is small and targeted; the read side builds the whole course in a
// fixed number of queries.
public sealed class CourseContentUseCases(
    ICourseContentRepository repository,
    LessonFiles files,
    TimeProvider timeProvider,
    ILogger<CourseContentUseCases> logger)
{
    public const int MaxUnitsPerCourse = 30;
    public const int MaxLessonsPerCourse = 200;

    private const string CourseNotFound = "No encontramos ese curso.";
    private const string UnitNotFound = "No encontramos esa unidad en el curso.";
    private const string LessonNotFound = "No encontramos esa lección en el curso.";
    private const string StorageUnavailable = "El almacenamiento de archivos no está configurado en este ambiente.";

    public async Task<ContentResult<CourseContentView>> GetContentAsync(Guid courseId,
        CancellationToken cancellationToken)
    {
        var content = await repository.GetContentAsync(courseId, cancellationToken);
        return content is null
            ? ContentResult<CourseContentView>.NotFound(CourseNotFound)
            : ContentResult<CourseContentView>.Ok(await files.SignAsync(content, cancellationToken));
    }

    public async Task<ContentResult<UnitView>> AddUnitAsync(Guid courseId, string title,
        CancellationToken cancellationToken)
    {
        if (!await repository.CourseExistsAsync(courseId, cancellationToken))
            return ContentResult<UnitView>.NotFound(CourseNotFound);

        var units = await repository.GetUnitsAsync(courseId, cancellationToken);
        if (units.Count >= MaxUnitsPerCourse)
            return ContentResult<UnitView>.Conflict($"Un curso admite como máximo {MaxUnitsPerCourse} unidades.");

        return await Guarded(async () =>
        {
            var position = units.Count == 0 ? 1 : units.Max(unit => unit.Position) + 1;
            var unit = CourseUnit.Create(Guid.NewGuid(), courseId, title, position, timeProvider.GetUtcNow());
            repository.Add(unit);
            await repository.SaveChangesAsync(cancellationToken);
            return ToView(unit);
        });
    }

    public async Task<ContentResult<UnitView>> RenameUnitAsync(Guid courseId, Guid unitId, string title,
        CancellationToken cancellationToken)
    {
        var unit = await repository.GetUnitAsync(courseId, unitId, cancellationToken);
        if (unit is null) return ContentResult<UnitView>.NotFound(UnitNotFound);

        return await Guarded(async () =>
        {
            unit.Rename(title, timeProvider.GetUtcNow());
            await repository.SaveChangesAsync(cancellationToken);
            return ToView(unit);
        });
    }

    public async Task<ContentResult<bool>> DeleteUnitAsync(Guid courseId, Guid unitId,
        CancellationToken cancellationToken)
    {
        var unit = await repository.GetUnitAsync(courseId, unitId, cancellationToken);
        if (unit is null) return ContentResult<bool>.NotFound(UnitNotFound);
        if (await repository.UnitHasLessonsAsync(unitId, cancellationToken))
            return ContentResult<bool>.Conflict("La unidad tiene lecciones: muévelas o bórralas antes de borrarla.");

        repository.Remove(unit);
        await repository.SaveChangesAsync(cancellationToken);
        return ContentResult<bool>.Ok(true);
    }

    /// <summary>The new order must list every unit of the course exactly once.</summary>
    public async Task<ContentResult<CourseContentView>> ReorderUnitsAsync(Guid courseId,
        IReadOnlyList<Guid> unitIds, CancellationToken cancellationToken)
    {
        if (!await repository.CourseExistsAsync(courseId, cancellationToken))
            return ContentResult<CourseContentView>.NotFound(CourseNotFound);

        var units = await repository.GetUnitsAsync(courseId, cancellationToken);
        if (!IsPermutation(unitIds, units.Select(unit => unit.Id)))
            return ContentResult<CourseContentView>.Invalid("El nuevo orden debe incluir cada unidad del curso una sola vez.");

        var now = timeProvider.GetUtcNow();
        var byId = units.ToDictionary(unit => unit.Id);
        for (var index = 0; index < unitIds.Count; index++) byId[unitIds[index]].MoveTo(index + 1, now);
        await repository.SaveChangesAsync(cancellationToken);
        return await GetContentAsync(courseId, cancellationToken);
    }

    /// <summary>A new lesson has no files yet: they are uploaded for an existing lesson, then attached with an update.</summary>
    public async Task<ContentResult<LessonView>> AddLessonAsync(Guid courseId, Guid unitId, LessonInput input,
        CancellationToken cancellationToken)
    {
        if (input.VideoFileId is not null || input.Materials.Any(material => material.FileId is not null))
            return ContentResult<LessonView>.Invalid("Crea la lección primero y después sube sus archivos.");

        var unit = await repository.GetUnitAsync(courseId, unitId, cancellationToken);
        if (unit is null) return ContentResult<LessonView>.NotFound(UnitNotFound);
        if (await repository.CountLessonsAsync(courseId, cancellationToken) >= MaxLessonsPerCourse)
            return ContentResult<LessonView>.Conflict($"Un curso admite como máximo {MaxLessonsPerCourse} lecciones.");

        var position = await repository.NextLessonPositionAsync(unitId, cancellationToken);
        var saved = await Guarded(async () =>
        {
            var lesson = Lesson.Create(Guid.NewGuid(), courseId, unitId, position, ToContent(input, null, new Dictionary<Guid, StoredFile>()),
                timeProvider.GetUtcNow());
            repository.Add(lesson);
            await repository.SaveChangesAsync(cancellationToken);
            return lesson.Id;
        });
        return await WithNumberAsync(courseId, saved, cancellationToken);
    }

    /// <summary>
    /// Replaces the lesson's content; a different <paramref name="unitId"/> moves it to the end of that unit. Files
    /// the lesson no longer uses are deleted from storage after the change is saved.
    /// </summary>
    public async Task<ContentResult<LessonView>> UpdateLessonAsync(Guid courseId, Guid lessonId, Guid unitId,
        LessonInput input, CancellationToken cancellationToken)
    {
        var lesson = await repository.GetLessonAsync(courseId, lessonId, cancellationToken);
        if (lesson is null) return ContentResult<LessonView>.NotFound(LessonNotFound);

        var moving = unitId != lesson.UnitId;
        if (moving && await repository.GetUnitAsync(courseId, unitId, cancellationToken) is null)
            return ContentResult<LessonView>.NotFound(UnitNotFound);
        var position = moving ? await repository.NextLessonPositionAsync(unitId, cancellationToken) : lesson.Position;

        var previous = lesson.Files();
        var resolved = await ResolveFilesAsync(courseId, lessonId, input, previous, cancellationToken);
        if (resolved.Status != ContentResultStatus.Ok) return new ContentResult<LessonView>(resolved.Status, null, resolved.Detail);

        var saved = await Guarded(async () =>
        {
            var now = timeProvider.GetUtcNow();
            lesson.Update(resolved.Value!, now);
            if (moving) lesson.MoveTo(unitId, position, now);
            await repository.SaveChangesAsync(cancellationToken);
            return lesson.Id;
        });
        if (saved.Status == ContentResultStatus.Ok)
            await DeleteUnusedAsync(courseId, lessonId, previous.Keys.Except(lesson.Files().Keys), cancellationToken);
        return await WithNumberAsync(courseId, saved, cancellationToken);
    }

    public async Task<ContentResult<bool>> DeleteLessonAsync(Guid courseId, Guid lessonId,
        CancellationToken cancellationToken)
    {
        var lesson = await repository.GetLessonAsync(courseId, lessonId, cancellationToken);
        if (lesson is null) return ContentResult<bool>.NotFound(LessonNotFound);

        var lessonFiles = lesson.Files().Keys.ToArray();
        repository.Remove(lesson);
        await repository.SaveChangesAsync(cancellationToken);
        await DeleteUnusedAsync(courseId, lessonId, lessonFiles, cancellationToken);
        return ContentResult<bool>.Ok(true);
    }

    /// <summary>The new order must list every lesson of the unit exactly once.</summary>
    public async Task<ContentResult<CourseContentView>> ReorderLessonsAsync(Guid courseId, Guid unitId,
        IReadOnlyList<Guid> lessonIds, CancellationToken cancellationToken)
    {
        if (await repository.GetUnitAsync(courseId, unitId, cancellationToken) is null)
            return ContentResult<CourseContentView>.NotFound(UnitNotFound);

        var lessons = await repository.GetUnitLessonsAsync(unitId, cancellationToken);
        if (!IsPermutation(lessonIds, lessons.Select(lesson => lesson.Id)))
            return ContentResult<CourseContentView>.Invalid("El nuevo orden debe incluir cada lección de la unidad una sola vez.");

        var now = timeProvider.GetUtcNow();
        var byId = lessons.ToDictionary(lesson => lesson.Id);
        for (var index = 0; index < lessonIds.Count; index++) byId[lessonIds[index]].MoveTo(unitId, index + 1, now);
        await repository.SaveChangesAsync(cancellationToken);
        return await GetContentAsync(courseId, cancellationToken);
    }

    /// <summary>
    /// Grants a direct upload of a video or material for an existing lesson (SCRUM-58). The file is attached by
    /// sending its id in the next lesson update, which checks it again in storage.
    /// </summary>
    public async Task<ContentResult<UploadGrant>> GrantUploadAsync(Guid courseId, Guid lessonId, string? kind,
        string? contentType, long size, CancellationToken cancellationToken)
    {
        if (!LessonFileRules.TryParseKind(kind, out var fileKind))
            return ContentResult<UploadGrant>.Invalid("Indica si el archivo es «video» o «material».");
        if (LessonFileRules.Problem(fileKind, contentType, size) is { } problem)
            return ContentResult<UploadGrant>.Invalid(problem);
        if (!files.Available) return ContentResult<UploadGrant>.Unavailable(StorageUnavailable);
        if (await repository.GetLessonAsync(courseId, lessonId, cancellationToken) is null)
            return ContentResult<UploadGrant>.NotFound(LessonNotFound);

        try
        {
            return ContentResult<UploadGrant>.Ok(await files.GrantUploadAsync(courseId, lessonId, fileKind, contentType!,
                size, cancellationToken));
        }
        catch (ArgumentException)
        {
            // Our rules allow it but this environment's storage configuration does not (AllowedContentTypes / size).
            return ContentResult<UploadGrant>.Invalid("El almacenamiento de este ambiente no acepta ese tipo o tamaño de archivo.");
        }
    }

    // Files already in the lesson are kept as they are; new ones must exist in storage and follow the rules.
    private async Task<ContentResult<LessonContent>> ResolveFilesAsync(Guid courseId, Guid lessonId, LessonInput input,
        IReadOnlyDictionary<Guid, StoredFile> known, CancellationToken cancellationToken)
    {
        var references = input.Materials.Where(material => material.FileId is not null)
            .Select(material => (Id: material.FileId!.Value, Kind: LessonFileKind.Material, material.Title))
            .ToList();
        if (input.VideoFileId is { } videoId) references.Add((videoId, LessonFileKind.Video, "video"));
        if (references.Select(reference => reference.Id).Distinct().Count() != references.Count)
            return ContentResult<LessonContent>.Invalid("Cada archivo se puede usar una sola vez en la lección.");

        var unknown = references.Where(reference => !known.ContainsKey(reference.Id)).ToArray();
        if (unknown.Length > 0 && !files.Available) return ContentResult<LessonContent>.Unavailable(StorageUnavailable);

        // One storage lookup per new file, all at the same time.
        var found = await Task.WhenAll(unknown.Select(async reference =>
            (reference, info: await files.FindAsync(courseId, lessonId, reference.Id, cancellationToken))));

        var resolved = new Dictionary<Guid, StoredFile>(known);
        foreach (var (reference, info) in found)
        {
            if (info is null)
                return ContentResult<LessonContent>.Invalid($"No encontramos el archivo de «{reference.Title}»: vuelve a subirlo.");
            resolved[reference.Id] = new StoredFile(reference.Id, info.ContentType, info.Size);
        }

        foreach (var reference in references)
        {
            var file = resolved[reference.Id];
            if (LessonFileRules.Problem(reference.Kind, file.ContentType, file.Size) is { } problem)
                return ContentResult<LessonContent>.Invalid($"«{reference.Title}»: {problem}");
        }

        return ContentResult<LessonContent>.Ok(ToContent(input, input.VideoFileId is { } video ? resolved[video] : null, resolved));
    }

    private static LessonContent ToContent(LessonInput input, StoredFile? videoFile, IReadOnlyDictionary<Guid, StoredFile> resolved) =>
        new(input.Title, input.Summary, input.VideoUrl, input.IsPublished,
            input.Materials.Select(material => new MaterialContent(material.Title, material.Url,
                material.FileId is { } fileId ? resolved[fileId] : null)).ToArray(),
            videoFile);

    private async Task DeleteUnusedAsync(Guid courseId, Guid lessonId, IEnumerable<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        var unused = fileIds.ToArray();
        if (unused.Length == 0) return;
        var failed = await files.DeleteAsync(courseId, lessonId, unused, cancellationToken);
        if (failed > 0)
            logger.LogWarning("Could not delete {Failed} of {Total} unused files of lesson {LessonId}.", failed, unused.Length, lessonId);
    }

    private static bool IsPermutation(IReadOnlyList<Guid>? requested, IEnumerable<Guid> existing)
    {
        if (requested is null) return false;
        var current = existing.ToHashSet();
        return requested.Count == current.Count && requested.ToHashSet().SetEquals(current);
    }

    // Domain rules throw ArgumentException with a message meant for the administrator.
    private static async Task<ContentResult<T>> Guarded<T>(Func<Task<T>> change)
    {
        try
        {
            return ContentResult<T>.Ok(await change());
        }
        catch (ArgumentException exception)
        {
            var message = exception.Message;
            var cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);
            return ContentResult<T>.Invalid(cut > 0 ? message[..cut] : message);
        }
    }

    private static UnitView ToView(CourseUnit unit) => new(unit.Id, unit.Title, unit.Position, []);

    // A lesson's number (its place in the whole course) depends on every unit and lesson before it, so the
    // saved lesson is read back from the course view. Content changes are rare; reads are the hot path.
    private async Task<ContentResult<LessonView>> WithNumberAsync(Guid courseId, ContentResult<Guid> saved,
        CancellationToken cancellationToken)
    {
        if (saved.Status != ContentResultStatus.Ok) return new ContentResult<LessonView>(saved.Status, null, saved.Detail);
        var content = await GetContentAsync(courseId, cancellationToken);
        var lesson = content.Value?.Units.SelectMany(unit => unit.Lessons).SingleOrDefault(item => item.Id == saved.Value);
        return lesson is null ? ContentResult<LessonView>.NotFound(LessonNotFound) : ContentResult<LessonView>.Ok(lesson);
    }
}
