using AulaVirtual.Modules.Courses.Domain.Content;
using AulaVirtual.Modules.Courses.Domain.Courses;
using AulaVirtual.Modules.Courses.Domain.Levels;

namespace AulaVirtual.Modules.Courses.Application.Levels;

/// <summary>A level and its course (null until provisioned), with how many of its lessons exist and are published.</summary>
public sealed record MathLevelView(
    string Stage,
    string Level,
    string StageLabel,
    string LevelLabel,
    int SessionMinutes,
    Guid? CourseId,
    string? CourseTitle,
    bool CoursePublished,
    int Lessons,
    int PublishedLessons);

public sealed record LevelOfCourse(Guid CourseId, string Stage, string Level, string StageLabel, string LevelLabel);

public sealed record ProvisionResult(int Created, IReadOnlyList<MathLevelView> Levels);

public sealed record LevelLink(EducationStage Stage, MathLevel Level, Guid CourseId, string CourseTitle, bool CoursePublished);

public interface IMathLevelRepository
{
    /// <summary>Every provisioned level with its course, in one query.</summary>
    Task<IReadOnlyList<LevelLink>> GetLinksAsync(CancellationToken cancellationToken);

    /// <summary>Lessons and published lessons per course, in one grouped query.</summary>
    Task<IReadOnlyDictionary<Guid, (int Total, int Published)>> CountLessonsAsync(IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<(Guid CourseId, EducationStage Stage, MathLevel Level)>> GetLevelsOfAsync(
        IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken);

    Task<CourseCategory?> FindCategoryAsync(string name, CancellationToken cancellationToken);

    void Add(object entity);

    /// <summary>False when someone else provisioned the same level at the same time (unique stage + level).</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

// Cross-module read contract: the level of the courses a student is enrolled in (for «Mi ruta» and the
// diagnostic). One query for any number of courses.
public interface IMathLevelDirectory
{
    Task<IReadOnlyList<LevelOfCourse>> GetLevelsAsync(IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken);
}

// The Mathematics levels (SCRUM-39). Provisioning creates only the missing levels, each as a draft course
// with one unit and its 14 draft sessions ready for the academic team to fill in (SCRUM-74). It can run any
// number of times: existing levels and their content are never touched.
public sealed class MathLevelUseCases(IMathLevelRepository repository, TimeProvider timeProvider) : IMathLevelDirectory
{
    public const string CategoryName = "Matemáticas";

    /// <summary>The eight levels in path order. Two queries.</summary>
    public async Task<IReadOnlyList<MathLevelView>> ListAsync(CancellationToken cancellationToken)
    {
        var links = (await repository.GetLinksAsync(cancellationToken)).ToDictionary(link => (link.Stage, link.Level));
        var counts = links.Count == 0
            ? new Dictionary<Guid, (int Total, int Published)>()
            : await repository.CountLessonsAsync(links.Values.Select(link => link.CourseId).ToArray(), cancellationToken);

        return MathLevels.All.Select(definition =>
        {
            var link = links.GetValueOrDefault((definition.Stage, definition.Level));
            var count = link is null ? default : counts.GetValueOrDefault(link.CourseId);
            return new MathLevelView(definition.StageCode, definition.LevelCode, definition.StageLabel, definition.LevelLabel,
                definition.SessionMinutes, link?.CourseId, link?.CourseTitle, link?.CoursePublished ?? false,
                count.Total, count.Published);
        }).ToArray();
    }

    /// <summary>Null when another administrator provisioned at the same time (nothing of this call was saved).</summary>
    public async Task<ProvisionResult?> ProvisionAsync(CancellationToken cancellationToken)
    {
        var existing = (await repository.GetLinksAsync(cancellationToken)).Select(link => (link.Stage, link.Level)).ToHashSet();
        var missing = MathLevels.All.Where(definition => !existing.Contains((definition.Stage, definition.Level))).ToArray();
        if (missing.Length == 0) return new ProvisionResult(0, await ListAsync(cancellationToken));

        var now = timeProvider.GetUtcNow();
        var category = await repository.FindCategoryAsync(CategoryName, cancellationToken);
        if (category is null)
        {
            category = CourseCategory.Create(Guid.NewGuid(), CategoryName, now);
            repository.Add(category);
        }

        foreach (var definition in missing)
        {
            var course = Course.Create(Guid.NewGuid(), definition.CourseTitle, category.Id, now);
            course.SetSessionDuration(definition.SessionMinutes, now);
            repository.Add(course);
            repository.Add(MathLevelCourse.Create(definition.Stage, definition.Level, course.Id, now));

            var unit = CourseUnit.Create(Guid.NewGuid(), course.Id, "Sesiones", 1, now);
            repository.Add(unit);
            for (var session = 1; session <= MathLevels.SessionsPerLevel; session++)
                repository.Add(Lesson.Create(Guid.NewGuid(), course.Id, unit.Id, session,
                    new LessonContent($"Sesión {session}", null, null, false, []), now));
        }

        // Everything in one transaction: either every missing level is created or none is.
        if (!await repository.TrySaveChangesAsync(cancellationToken)) return null;
        return new ProvisionResult(missing.Length, await ListAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<LevelOfCourse>> GetLevelsAsync(IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        if (courseIds.Count == 0) return [];
        return (await repository.GetLevelsOfAsync(courseIds, cancellationToken))
            .Select(row =>
            {
                var definition = new MathLevelDefinition(row.Stage, row.Level);
                return new LevelOfCourse(row.CourseId, definition.StageCode, definition.LevelCode, definition.StageLabel,
                    definition.LevelLabel);
            })
            .ToArray();
    }
}
