using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Domain.Courses;
using AulaVirtual.Modules.Courses.Domain.Levels;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

public sealed class MathLevelRepository(CoursesDbContext db) : IMathLevelRepository
{
    public async Task<IReadOnlyList<LevelLink>> GetLinksAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from link in db.MathLevelCourses.AsNoTracking()
                join course in db.Courses.AsNoTracking() on link.CourseId equals course.Id
                select new { link.Stage, link.Level, link.CourseId, course.Title, course.IsPublished })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new LevelLink(row.Stage, row.Level, row.CourseId, row.Title, row.IsPublished)).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, (int Total, int Published)>> CountLessonsAsync(
        IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
    {
        var ids = courseIds.ToArray();
        var rows = await db.Lessons.AsNoTracking()
            .Where(lesson => ids.Contains(lesson.CourseId))
            .GroupBy(lesson => lesson.CourseId)
            .Select(group => new { CourseId = group.Key, Total = group.Count(), Published = group.Count(lesson => lesson.IsPublished) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.CourseId, row => (row.Total, row.Published));
    }

    public async Task<IReadOnlyList<(Guid CourseId, EducationStage Stage, MathLevel Level)>> GetLevelsOfAsync(
        IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
    {
        var ids = courseIds.ToArray();
        var rows = await db.MathLevelCourses.AsNoTracking()
            .Where(link => ids.Contains(link.CourseId))
            .Select(link => new { link.CourseId, link.Stage, link.Level })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.CourseId, row.Stage, row.Level)).ToArray();
    }

    public Task<CourseCategory?> FindCategoryAsync(string name, CancellationToken cancellationToken) =>
        db.Categories.FirstOrDefaultAsync(category => category.Name == name, cancellationToken);

    public void Add(object entity) => db.Add(entity);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                   {
                                                       SqlState: PostgresErrorCodes.UniqueViolation
                                                   })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
