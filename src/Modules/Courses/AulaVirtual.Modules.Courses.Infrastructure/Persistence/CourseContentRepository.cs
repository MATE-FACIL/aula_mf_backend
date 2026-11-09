using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

public sealed class CourseContentRepository(CoursesDbContext db) : ICourseContentRepository, ICourseContentDirectory
{
    public Task<bool> CourseExistsAsync(Guid courseId, CancellationToken cancellationToken) =>
        db.Courses.AsNoTracking().AnyAsync(course => course.Id == courseId, cancellationToken);

    public Task<CourseContentView?> GetContentAsync(Guid courseId, CancellationToken cancellationToken) =>
        ReadAsync(courseId, publishedOnly: false, cancellationToken);

    public Task<CourseContentView?> GetPublishedContentAsync(Guid courseId, CancellationToken cancellationToken) =>
        ReadAsync(courseId, publishedOnly: true, cancellationToken);

    public async Task<IReadOnlyList<CourseUnit>> GetUnitsAsync(Guid courseId, CancellationToken cancellationToken) =>
        await db.CourseUnits.Where(unit => unit.CourseId == courseId)
            .OrderBy(unit => unit.Position).ThenBy(unit => unit.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<CourseUnit?> GetUnitAsync(Guid courseId, Guid unitId, CancellationToken cancellationToken) =>
        db.CourseUnits.SingleOrDefaultAsync(unit => unit.Id == unitId && unit.CourseId == courseId, cancellationToken);

    public Task<Lesson?> GetLessonAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken) =>
        db.Lessons.Include(lesson => lesson.Materials)
            .SingleOrDefaultAsync(lesson => lesson.Id == lessonId && lesson.CourseId == courseId, cancellationToken);

    public async Task<IReadOnlyList<Lesson>> GetUnitLessonsAsync(Guid unitId, CancellationToken cancellationToken) =>
        await db.Lessons.Where(lesson => lesson.UnitId == unitId)
            .OrderBy(lesson => lesson.Position).ThenBy(lesson => lesson.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountLessonsAsync(Guid courseId, CancellationToken cancellationToken) =>
        db.Lessons.CountAsync(lesson => lesson.CourseId == courseId, cancellationToken);

    public Task<bool> UnitHasLessonsAsync(Guid unitId, CancellationToken cancellationToken) =>
        db.Lessons.AnyAsync(lesson => lesson.UnitId == unitId, cancellationToken);

    public async Task<int> NextLessonPositionAsync(Guid unitId, CancellationToken cancellationToken) =>
        (await db.Lessons.Where(lesson => lesson.UnitId == unitId).MaxAsync(lesson => (int?)lesson.Position, cancellationToken) ?? 0) + 1;

    public void Add(CourseUnit unit) => db.CourseUnits.Add(unit);

    public void Add(Lesson lesson) => db.Lessons.Add(lesson);

    public void Remove(CourseUnit unit) => db.CourseUnits.Remove(unit);

    public void Remove(Lesson lesson) => db.Lessons.Remove(lesson);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    // Four small queries whatever the size of the course: course, units, lessons, materials. Never one per unit or
    // per lesson. Filter and order on the entities, project last (EF cannot translate filters over records).
    private async Task<CourseContentView?> ReadAsync(Guid courseId, bool publishedOnly, CancellationToken cancellationToken)
    {
        var course = await db.Courses.AsNoTracking()
            .Where(item => item.Id == courseId)
            .Select(item => new { item.Id, item.Title })
            .SingleOrDefaultAsync(cancellationToken);
        if (course is null) return null;

        var units = await db.CourseUnits.AsNoTracking()
            .Where(unit => unit.CourseId == courseId)
            .OrderBy(unit => unit.Position).ThenBy(unit => unit.CreatedAt)
            .Select(unit => new { unit.Id, unit.Title, unit.Position })
            .ToListAsync(cancellationToken);
        if (units.Count == 0) return new CourseContentView(course.Id, course.Title, []);

        var lessonsQuery = db.Lessons.AsNoTracking().Where(lesson => lesson.CourseId == courseId);
        if (publishedOnly) lessonsQuery = lessonsQuery.Where(lesson => lesson.IsPublished);
        var lessons = await lessonsQuery
            .OrderBy(lesson => lesson.Position).ThenBy(lesson => lesson.CreatedAt)
            .Select(lesson => new
            {
                lesson.Id, lesson.UnitId, lesson.Title, lesson.Summary, lesson.VideoUrl, lesson.IsPublished
            })
            .ToListAsync(cancellationToken);

        var lessonIds = lessons.Select(lesson => lesson.Id).ToArray();
        var materials = lessonIds.Length == 0
            ? []
            : await db.LessonMaterials.AsNoTracking()
                .Where(material => lessonIds.Contains(material.LessonId))
                .OrderBy(material => material.Position)
                .Select(material => new { material.Id, material.LessonId, material.Title, material.Url })
                .ToListAsync(cancellationToken);

        var materialsByLesson = materials.ToLookup(material => material.LessonId);
        var lessonsByUnit = lessons.ToLookup(lesson => lesson.UnitId);
        var number = 0;
        var unitViews = new List<UnitView>(units.Count);
        foreach (var unit in units)
        {
            var unitLessons = lessonsByUnit[unit.Id]
                .Select(lesson => new LessonView(lesson.Id, lesson.UnitId, ++number, lesson.Title, lesson.Summary,
                    lesson.VideoUrl, lesson.IsPublished,
                    materialsByLesson[lesson.Id].Select(item => new MaterialView(item.Id, item.Title, item.Url)).ToArray()))
                .ToArray();
            // Students never see an empty unit; the administrator sees every unit to fill it in.
            if (publishedOnly && unitLessons.Length == 0) continue;
            unitViews.Add(new UnitView(unit.Id, unit.Title, unit.Position, unitLessons));
        }

        return new CourseContentView(course.Id, course.Title, unitViews);
    }
}
