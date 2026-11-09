using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Domain.Content;
using AulaVirtual.Modules.Courses.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Tests;

public sealed class LessonDomainTests
{
    private static readonly Guid Course = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    private static LessonContent Content(string? video = null, params MaterialContent[] materials) =>
        new("  Sumar fracciones  ", "  Con distinto denominador ", video, true, materials);

    [Fact]
    public void Create_TrimsTexts_AndKeepsMaterialOrder()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), Course, Unit, 1,
            Content("https://www.youtube.com/watch?v=abc",
                new MaterialContent("Guía", "https://cdn.example.com/guia.pdf"),
                new MaterialContent("Pizarra", "https://cdn.example.com/pizarra.png")),
            DateTimeOffset.UtcNow);

        Assert.Equal("Sumar fracciones", lesson.Title);
        Assert.Equal("Con distinto denominador", lesson.Summary);
        Assert.Equal(["Guía", "Pizarra"], lesson.Materials.OrderBy(m => m.Position).Select(m => m.Title));
        Assert.All(lesson.Materials, material => Assert.Equal(lesson.Id, material.LessonId));
    }

    [Theory]
    [InlineData("http://example.com/video.mp4")]
    [InlineData("javascript:alert(1)")]
    [InlineData("no es un enlace")]
    public void Video_MustBeHttps(string video) =>
        Assert.Throws<ArgumentException>(() => Lesson.Create(Guid.NewGuid(), Course, Unit, 1, Content(video), DateTimeOffset.UtcNow));

    [Fact]
    public void Update_ReplacesMaterials_AndABadMaterialChangesNothing()
    {
        var lesson = Lesson.Create(Guid.NewGuid(), Course, Unit, 1,
            Content(null, new MaterialContent("Guía", "https://cdn.example.com/guia.pdf")), DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => lesson.Update(
            new LessonContent("Otro título", null, null, false, [new MaterialContent("Malo", "http://inseguro.example.com")]),
            DateTimeOffset.UtcNow));
        Assert.Equal("Sumar fracciones", lesson.Title);
        Assert.Single(lesson.Materials);

        lesson.Update(new LessonContent("Otro título", null, null, false, []), DateTimeOffset.UtcNow);
        Assert.Empty(lesson.Materials);
        Assert.False(lesson.IsPublished);
    }

    [Fact]
    public void Materials_AreCapped()
    {
        var many = Enumerable.Range(1, Lesson.MaxMaterials + 1)
            .Select(i => new MaterialContent($"Archivo {i}", $"https://cdn.example.com/{i}.pdf")).ToArray();
        Assert.Throws<ArgumentException>(() => Lesson.Create(Guid.NewGuid(), Course, Unit, 1, Content(null, many), DateTimeOffset.UtcNow));
    }
}

public sealed class CourseContentUseCaseTests
{
    private readonly InMemoryContent repository = new();
    private readonly Guid course = Guid.NewGuid();

    public CourseContentUseCaseTests() => repository.Courses.Add(course, "Matemáticas · Intermedio");

    private CourseContentUseCases UseCases() => new(repository, TimeProvider.System);

    private static LessonContent Lesson(string title, bool published = true) => new(title, null, null, published, []);

    [Fact]
    public async Task Lessons_AreNumberedAcrossUnits_InUnitThenLessonOrder()
    {
        var useCases = UseCases();
        var first = (await useCases.AddUnitAsync(course, "Unidad 1", default)).Value!;
        var second = (await useCases.AddUnitAsync(course, "Unidad 2", default)).Value!;
        await useCases.AddLessonAsync(course, second.Id, Lesson("C"), default);
        await useCases.AddLessonAsync(course, first.Id, Lesson("A"), default);
        var b = (await useCases.AddLessonAsync(course, first.Id, Lesson("B"), default)).Value!;

        Assert.Equal(2, b.Number);
        var content = (await useCases.GetContentAsync(course, default)).Value!;
        Assert.Equal(["A", "B", "C"], content.Units.SelectMany(u => u.Lessons).Select(l => l.Title));
        Assert.Equal([1, 2, 3], content.Units.SelectMany(u => u.Lessons).Select(l => l.Number));

        var reordered = (await useCases.ReorderUnitsAsync(course, [second.Id, first.Id], default)).Value!;
        Assert.Equal(["C", "A", "B"], reordered.Units.SelectMany(u => u.Lessons).Select(l => l.Title));
    }

    [Fact]
    public async Task Reorder_MustListEveryItemOnce()
    {
        var useCases = UseCases();
        var unit = (await useCases.AddUnitAsync(course, "Unidad 1", default)).Value!;
        var a = (await useCases.AddLessonAsync(course, unit.Id, Lesson("A"), default)).Value!;
        await useCases.AddLessonAsync(course, unit.Id, Lesson("B"), default);

        Assert.Equal(ContentResultStatus.Invalid, (await useCases.ReorderLessonsAsync(course, unit.Id, [a.Id], default)).Status);
        Assert.Equal(ContentResultStatus.Invalid, (await useCases.ReorderLessonsAsync(course, unit.Id, [a.Id, a.Id], default)).Status);
        Assert.Equal(ContentResultStatus.Invalid, (await useCases.ReorderUnitsAsync(course, [], default)).Status);
    }

    [Fact]
    public async Task UnitWithLessons_CannotBeDeleted_ButAnEmptyOneCan()
    {
        var useCases = UseCases();
        var unit = (await useCases.AddUnitAsync(course, "Unidad 1", default)).Value!;
        var lesson = (await useCases.AddLessonAsync(course, unit.Id, Lesson("A"), default)).Value!;

        Assert.Equal(ContentResultStatus.Conflict, (await useCases.DeleteUnitAsync(course, unit.Id, default)).Status);
        await useCases.DeleteLessonAsync(course, lesson.Id, default);
        Assert.Equal(ContentResultStatus.Ok, (await useCases.DeleteUnitAsync(course, unit.Id, default)).Status);
    }

    [Fact]
    public async Task MovingALesson_PutsItAtTheEndOfTheOtherUnit()
    {
        var useCases = UseCases();
        var first = (await useCases.AddUnitAsync(course, "Unidad 1", default)).Value!;
        var second = (await useCases.AddUnitAsync(course, "Unidad 2", default)).Value!;
        var a = (await useCases.AddLessonAsync(course, first.Id, Lesson("A"), default)).Value!;
        await useCases.AddLessonAsync(course, second.Id, Lesson("B"), default);

        var moved = (await useCases.UpdateLessonAsync(course, a.Id, second.Id, Lesson("A"), default)).Value!;

        Assert.Equal(second.Id, moved.UnitId);
        Assert.Equal(2, moved.Number);
    }

    [Fact]
    public async Task OtherCoursesUnitsAndLessons_AreNotFound()
    {
        var useCases = UseCases();
        var other = Guid.NewGuid();
        repository.Courses.Add(other, "Otro curso");
        var foreignUnit = (await useCases.AddUnitAsync(other, "Unidad ajena", default)).Value!;

        Assert.Equal(ContentResultStatus.NotFound, (await useCases.AddLessonAsync(course, foreignUnit.Id, Lesson("A"), default)).Status);
        Assert.Equal(ContentResultStatus.NotFound, (await useCases.AddUnitAsync(Guid.NewGuid(), "Unidad", default)).Status);
    }

    [Fact]
    public async Task InvalidContent_IsReportedWithAReadableMessage()
    {
        var useCases = UseCases();
        var unit = (await useCases.AddUnitAsync(course, "Unidad 1", default)).Value!;

        var result = await useCases.AddLessonAsync(course, unit.Id,
            new LessonContent("A", null, "http://video.example.com", true, []), default);

        Assert.Equal(ContentResultStatus.Invalid, result.Status);
        Assert.Equal("El video debe ser un enlace https:// válido.", result.Detail);
        Assert.Equal(ContentResultStatus.Invalid, (await useCases.AddUnitAsync(course, "   ", default)).Status);
    }

    [Fact]
    public async Task UnitsArePerCourseCapped()
    {
        var useCases = UseCases();
        for (var i = 0; i < CourseContentUseCases.MaxUnitsPerCourse; i++)
            await useCases.AddUnitAsync(course, $"Unidad {i}", default);

        Assert.Equal(ContentResultStatus.Conflict, (await useCases.AddUnitAsync(course, "Una más", default)).Status);
    }

    private sealed class InMemoryContent : ICourseContentRepository
    {
        public Dictionary<Guid, string> Courses { get; } = [];
        private readonly List<CourseUnit> units = [];
        private readonly List<Lesson> lessons = [];

        public Task<bool> CourseExistsAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(Courses.ContainsKey(courseId));

        public Task<CourseContentView?> GetContentAsync(Guid courseId, CancellationToken cancellationToken)
        {
            if (!Courses.TryGetValue(courseId, out var title)) return Task.FromResult<CourseContentView?>(null);
            var number = 0;
            var view = units.Where(u => u.CourseId == courseId).OrderBy(u => u.Position)
                .Select(u => new UnitView(u.Id, u.Title, u.Position, lessons.Where(l => l.UnitId == u.Id).OrderBy(l => l.Position)
                    .Select(l => new LessonView(l.Id, l.UnitId, ++number, l.Title, l.Summary, l.VideoUrl, l.IsPublished, []))
                    .ToArray()))
                .ToArray();
            return Task.FromResult<CourseContentView?>(new CourseContentView(courseId, title, view));
        }

        public Task<IReadOnlyList<CourseUnit>> GetUnitsAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseUnit>>(units.Where(u => u.CourseId == courseId).OrderBy(u => u.Position).ToList());

        public Task<CourseUnit?> GetUnitAsync(Guid courseId, Guid unitId, CancellationToken cancellationToken) =>
            Task.FromResult(units.SingleOrDefault(u => u.Id == unitId && u.CourseId == courseId));

        public Task<Lesson?> GetLessonAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken) =>
            Task.FromResult(lessons.SingleOrDefault(l => l.Id == lessonId && l.CourseId == courseId));

        public Task<IReadOnlyList<Lesson>> GetUnitLessonsAsync(Guid unitId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Lesson>>(lessons.Where(l => l.UnitId == unitId).OrderBy(l => l.Position).ToList());

        public Task<int> CountLessonsAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(lessons.Count(l => l.CourseId == courseId));

        public Task<bool> UnitHasLessonsAsync(Guid unitId, CancellationToken cancellationToken) => Task.FromResult(lessons.Any(l => l.UnitId == unitId));

        public Task<int> NextLessonPositionAsync(Guid unitId, CancellationToken cancellationToken) =>
            Task.FromResult(lessons.Where(l => l.UnitId == unitId).Select(l => l.Position).DefaultIfEmpty(0).Max() + 1);

        public void Add(CourseUnit unit) => units.Add(unit);
        public void Add(Lesson lesson) => lessons.Add(lesson);
        public void Remove(CourseUnit unit) => units.Remove(unit);
        public void Remove(Lesson lesson) => lessons.Remove(lesson);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

// The course view must translate to SQL and stay at a fixed number of statements (no query per unit or lesson).
public sealed class CourseContentQueryTests
{
    private readonly SqlCapture capture = new();

    private CourseContentRepository Repository() => new(new CoursesDbContext(
        new DbContextOptionsBuilder<CoursesDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model")
            .AddInterceptors(capture)
            .Options));

    [Fact]
    public async Task PublishedContent_StartsWithTheCourse()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().GetPublishedContentAsync(Guid.NewGuid(), default));
        Assert.Contains("courses", sql);
        Assert.Contains("title", sql);
    }

    [Fact]
    public async Task NextPosition_IsAMaxQuery_ServedByTheUnitIndex()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Repository().NextLessonPositionAsync(Guid.NewGuid(), default));
        Assert.Contains("max(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unit_id", sql);
    }

    [Fact]
    public void Model_HasTheIndexesTheQueriesUse()
    {
        using var db = new CoursesDbContext(new DbContextOptionsBuilder<CoursesDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model").Options);
        var lessonIndexes = db.Model.FindEntityType(typeof(Lesson))!.GetIndexes().Select(index => index.GetDatabaseName()).ToArray();

        Assert.Contains("ix_lessons_course_published", lessonIndexes);
        Assert.Contains("ix_lessons_unit_position", lessonIndexes);
    }
}
