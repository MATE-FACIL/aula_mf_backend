using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Domain.Content;
using AulaVirtual.Modules.Courses.Domain.Courses;
using AulaVirtual.Modules.Courses.Domain.Levels;
using AulaVirtual.Modules.Courses.Infrastructure.Persistence;
using AulaVirtual.SharedKernel.Application.Storage;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AulaVirtual.Modules.Courses.Tests;

public sealed class LessonFileRulesTests
{
    [Theory]
    [InlineData("material", "application/pdf", 1024, true)]
    [InlineData("material", "video/mp4", 1024, false)]
    [InlineData("material", "application/pdf", LessonFileRules.MaxMaterialBytes + 1, false)]
    [InlineData("video", "video/mp4", LessonFileRules.MaxVideoBytes, true)]
    [InlineData("video", "video/quicktime", 1024, false)]
    [InlineData("video", "video/webm", 0, false)]
    public void TypesAndSizes_DependOnTheKind(string kind, string contentType, long size, bool accepted)
    {
        Assert.True(LessonFileRules.TryParseKind(kind, out var fileKind));
        Assert.Equal(accepted, LessonFileRules.Problem(fileKind, contentType, size) is null);
    }

    [Fact]
    public void AMaterialIsALinkOrAFile_NeverBothOrNeither()
    {
        var file = new StoredFile(Guid.NewGuid(), "application/pdf", 10);
        Lesson Make(params MaterialContent[] materials) => Lesson.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            new LessonContent("Lección", null, null, true, materials), DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => Make(new MaterialContent("Ambos", "https://cdn.example.com/a.pdf", file)));
        Assert.Throws<ArgumentException>(() => Make(new MaterialContent("Ninguno", null)));
        var lesson = Make(new MaterialContent("Archivo", null, file), new MaterialContent("Enlace", "https://cdn.example.com/b.pdf"));
        Assert.Equal([file.FileId], lesson.Files().Keys);
    }

    [Fact]
    public void TheVideoIsALinkOrAFile_NotBoth() =>
        Assert.Throws<ArgumentException>(() => Lesson.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            new LessonContent("Lección", null, "https://youtube.com/watch?v=a", true, [],
                new StoredFile(Guid.NewGuid(), "video/mp4", 10)), DateTimeOffset.UtcNow));
}

public sealed class LessonFilesUseCaseTests
{
    private readonly CourseContentUseCaseTests.InMemoryContent repository = new();
    private readonly FakeStorage storage = new();
    private readonly Guid course = Guid.NewGuid();

    public LessonFilesUseCaseTests() => repository.Courses.Add(course, "Matemáticas");

    private CourseContentUseCases UseCases(IFileStorage? fileStorage) =>
        new(repository, new LessonFiles(fileStorage), TimeProvider.System, NullLogger<CourseContentUseCases>.Instance);

    private async Task<LessonView> NewLessonAsync(CourseContentUseCases useCases)
    {
        var unit = (await useCases.AddUnitAsync(course, "Unidad 1", default)).Value!;
        return (await useCases.AddLessonAsync(course, unit.Id, new LessonInput("Sumar fracciones", null, null, null, true, []), default)).Value!;
    }

    private static LessonInput WithFiles(Guid? video, params MaterialInput[] materials) =>
        new("Sumar fracciones", null, null, video, true, materials);

    [Fact]
    public async Task Upload_IsGrantedOnlyForAllowedFiles_OfAnExistingLesson()
    {
        var useCases = UseCases(storage);
        var lesson = await NewLessonAsync(useCases);

        var grant = await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/pdf", 2048, default);
        Assert.Equal(ContentResultStatus.Ok, grant.Status);
        Assert.Equal("PUT", grant.Value!.Method);
        Assert.Equal(LessonFileRules.MaxMaterialBytes, grant.Value.MaxBytes);
        Assert.Contains($"courses/{course:N}/lessons/{lesson.Id:N}/files/{grant.Value.FileId:N}", grant.Value.Url.AbsoluteUri);

        Assert.Equal(ContentResultStatus.Invalid, (await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/x-msdownload", 10, default)).Status);
        Assert.Equal(ContentResultStatus.Invalid, (await useCases.GrantUploadAsync(course, lesson.Id, "foto", "image/png", 10, default)).Status);
        Assert.Equal(ContentResultStatus.NotFound, (await useCases.GrantUploadAsync(course, Guid.NewGuid(), "video", "video/mp4", 10, default)).Status);
    }

    [Fact]
    public async Task WithoutStorage_UploadsAreUnavailable_ButLinksStillWork()
    {
        var useCases = UseCases(null);
        var lesson = await NewLessonAsync(useCases);

        Assert.Equal(ContentResultStatus.Unavailable, (await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/pdf", 10, default)).Status);
        Assert.Equal(ContentResultStatus.Unavailable,
            (await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId, WithFiles(null, new MaterialInput("Guía", null, Guid.NewGuid())), default)).Status);
        Assert.Equal(ContentResultStatus.Ok,
            (await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId, WithFiles(null, new MaterialInput("Guía", "https://cdn.example.com/g.pdf", null)), default)).Status);
    }

    [Fact]
    public async Task UploadedFiles_AreAttached_WithWhatStorageReallyHolds_AndSigned()
    {
        var useCases = UseCases(storage);
        var lesson = await NewLessonAsync(useCases);
        var guide = (await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/pdf", 2048, default)).Value!;
        var video = (await useCases.GrantUploadAsync(course, lesson.Id, "video", "video/mp4", 4096, default)).Value!;
        storage.Upload(course, lesson.Id, guide.FileId, "application/pdf", 1500);
        storage.Upload(course, lesson.Id, video.FileId, "video/mp4", 4000);

        var result = await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId,
            WithFiles(video.FileId, new MaterialInput("Guía de práctica", null, guide.FileId)), default);

        Assert.Equal(ContentResultStatus.Ok, result.Status);
        var material = Assert.Single(result.Value!.Materials);
        Assert.Equal(1500, material.Size);
        Assert.StartsWith("https://storage.test/", material.Url);
        Assert.Contains("signed", material.Url);
        Assert.Equal(video.FileId, result.Value.VideoFileId);
        Assert.Contains("signed", result.Value.VideoUrl);
    }

    [Fact]
    public async Task AFileThatWasNeverUploaded_OrBreaksTheRules_IsRejected()
    {
        var useCases = UseCases(storage);
        var lesson = await NewLessonAsync(useCases);

        var missing = await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId,
            WithFiles(null, new MaterialInput("Guía", null, Guid.NewGuid())), default);
        Assert.Equal(ContentResultStatus.Invalid, missing.Status);
        Assert.Contains("vuelve a subirlo", missing.Detail);

        // The grant said PDF, but what arrived in storage is an executable.
        var grant = (await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/pdf", 10, default)).Value!;
        storage.Upload(course, lesson.Id, grant.FileId, "application/x-msdownload", 10);
        var wrongType = await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId,
            WithFiles(null, new MaterialInput("Guía", null, grant.FileId)), default);
        Assert.Equal(ContentResultStatus.Invalid, wrongType.Status);
    }

    [Fact]
    public async Task ReplacedAndDeletedFiles_AreRemovedFromStorage()
    {
        var useCases = UseCases(storage);
        var lesson = await NewLessonAsync(useCases);
        var first = (await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/pdf", 10, default)).Value!;
        var second = (await useCases.GrantUploadAsync(course, lesson.Id, "material", "application/pdf", 10, default)).Value!;
        storage.Upload(course, lesson.Id, first.FileId, "application/pdf", 10);
        storage.Upload(course, lesson.Id, second.FileId, "application/pdf", 10);

        await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId, WithFiles(null, new MaterialInput("A", null, first.FileId)), default);
        await useCases.UpdateLessonAsync(course, lesson.Id, lesson.UnitId, WithFiles(null, new MaterialInput("B", null, second.FileId)), default);
        Assert.Equal([first.FileId], storage.Deleted);

        await useCases.DeleteLessonAsync(course, lesson.Id, default);
        Assert.Equal([first.FileId, second.FileId], storage.Deleted);
    }

    [Fact]
    public async Task ANewLesson_CannotCarryFiles_YetBecauseTheyBelongToTheLesson() =>
        Assert.Equal(ContentResultStatus.Invalid, (await UseCases(storage).AddLessonAsync(course, Guid.NewGuid(),
            WithFiles(Guid.NewGuid()), default)).Status);

    private sealed class FakeStorage : IFileStorage
    {
        private readonly Dictionary<string, FileObjectInfo> objects = [];
        public List<Guid> Deleted { get; } = [];

        public void Upload(Guid courseId, Guid lessonId, Guid fileId, string contentType, long size)
        {
            var key = FileObjectKeys.LessonFile(courseId, lessonId, fileId);
            objects[key] = new FileObjectInfo(key, FileVisibility.Private, contentType, size, DateTimeOffset.UtcNow);
        }

        public Task<DirectUploadGrant> CreateDirectUploadAsync(DirectUploadRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectUploadGrant(new Uri($"https://storage.test/{request.ObjectKey}?upload"), "PUT",
                new Dictionary<string, string> { ["Content-Type"] = request.ContentType }, DateTimeOffset.UtcNow.Add(request.ExpiresIn)));

        public Task<FileObjectInfo?> GetInfoAsync(string objectKey, FileVisibility visibility, CancellationToken cancellationToken = default) =>
            Task.FromResult(objects.GetValueOrDefault(objectKey));

        public Task<Uri> GetPrivateDownloadUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri($"https://storage.test/{objectKey}?signed"));

        public Task DeleteAsync(string objectKey, FileVisibility visibility, CancellationToken cancellationToken = default)
        {
            objects.Remove(objectKey);
            Deleted.Add(Guid.ParseExact(objectKey.Split('/')[^1], "N"));
            return Task.CompletedTask;
        }

        public Task<StoredObject> StoreAsync(StoreFileRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Uri? GetPublicUrl(string objectKey) => throw new NotSupportedException();
    }
}

public sealed class MathLevelTests
{
    private readonly InMemoryLevels repository = new();

    private MathLevelUseCases UseCases() => new(repository, TimeProvider.System);

    [Fact]
    public void ThereAreFourLevelsPerStage_WithTheirClassLength()
    {
        Assert.Equal(8, MathLevels.All.Count);
        Assert.Equal(["prebasic", "basic", "intermediate", "advanced"],
            MathLevels.All.Where(l => l.Stage == EducationStage.Primary).Select(l => l.LevelCode));
        Assert.All(MathLevels.All.Where(l => l.Stage == EducationStage.Primary), l => Assert.Equal(60, l.SessionMinutes));
        Assert.All(MathLevels.All.Where(l => l.Stage == EducationStage.Secondary), l => Assert.Equal(90, l.SessionMinutes));
        Assert.Equal("Matemáticas · Secundaria Intermedio",
            new MathLevelDefinition(EducationStage.Secondary, MathLevel.Intermediate).CourseTitle);
    }

    [Fact]
    public async Task Provision_CreatesEveryLevelWithItsFourteenDraftSessions_InOneSave()
    {
        var result = await UseCases().ProvisionAsync(default);

        Assert.Equal(8, result!.Created);
        Assert.Equal(1, repository.Saves);
        Assert.Single(repository.Added.OfType<CourseCategory>());
        Assert.Equal(8, repository.Added.OfType<Course>().Count());
        var lessons = repository.Added.OfType<Lesson>().ToArray();
        Assert.Equal(8 * MathLevels.SessionsPerLevel, lessons.Length);
        Assert.All(lessons, lesson => Assert.False(lesson.IsPublished));
        Assert.All(result.Levels, level => Assert.NotNull(level.CourseId));
    }

    [Fact]
    public async Task Provision_IsSafeToRepeat_AndOnlyFillsWhatIsMissing()
    {
        await UseCases().ProvisionAsync(default);
        var again = await UseCases().ProvisionAsync(default);

        Assert.Equal(0, again!.Created);
        Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task Provision_ReusesTheMathematicsCategory()
    {
        repository.Category = CourseCategory.Create(Guid.NewGuid(), MathLevelUseCases.CategoryName, DateTimeOffset.UtcNow);
        await UseCases().ProvisionAsync(default);

        Assert.Empty(repository.Added.OfType<CourseCategory>());
        Assert.All(repository.Added.OfType<Course>(), course => Assert.Equal(repository.Category.Id, course.CategoryId));
    }

    [Fact]
    public async Task ConcurrentProvision_IsReportedWithoutSavingHalfTheLevels()
    {
        repository.FailNextSave = true;
        Assert.Null(await UseCases().ProvisionAsync(default));
        Assert.Empty(repository.Links);
    }

    [Fact]
    public async Task Levels_AndLessonCounts_AreTwoQueries()
    {
        var sql = await SqlCapture.RunAsync(capture, () => new MathLevelRepository(Db()).GetLinksAsync(default));
        Assert.Contains("JOIN", sql);
        sql = await SqlCapture.RunAsync(capture, () => new MathLevelRepository(Db()).CountLessonsAsync([Guid.NewGuid()], default));
        Assert.Contains("GROUP BY", sql);
    }

    private readonly SqlCapture capture = new();

    private CoursesDbContext Db() => new(new DbContextOptionsBuilder<CoursesDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options);

    private sealed class InMemoryLevels : IMathLevelRepository
    {
        public List<object> Added { get; } = [];
        public List<LevelLink> Links { get; } = [];
        public CourseCategory? Category { get; set; }
        public bool FailNextSave { get; set; }
        public int Saves { get; private set; }
        private readonly List<object> pending = [];

        public Task<IReadOnlyList<LevelLink>> GetLinksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LevelLink>>(Links.ToList());

        public Task<IReadOnlyDictionary<Guid, (int Total, int Published)>> CountLessonsAsync(IReadOnlyCollection<Guid> courseIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, (int Total, int Published)>>(Added.OfType<Lesson>()
                .Where(l => courseIds.Contains(l.CourseId)).GroupBy(l => l.CourseId)
                .ToDictionary(g => g.Key, g => (g.Count(), g.Count(l => l.IsPublished))));

        public Task<IReadOnlyList<(Guid CourseId, EducationStage Stage, MathLevel Level)>> GetLevelsOfAsync(IReadOnlyCollection<Guid> courseIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CourseCategory?> FindCategoryAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Category);

        public void Add(object entity) => pending.Add(entity);

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                pending.Clear();
                return Task.FromResult(false);
            }

            Added.AddRange(pending);
            var courses = pending.OfType<Course>().ToDictionary(course => course.Id);
            Links.AddRange(pending.OfType<MathLevelCourse>().Select(link =>
                new LevelLink(link.Stage, link.Level, link.CourseId, courses[link.CourseId].Title, false)));
            pending.Clear();
            Saves++;
            return Task.FromResult(true);
        }
    }
}
