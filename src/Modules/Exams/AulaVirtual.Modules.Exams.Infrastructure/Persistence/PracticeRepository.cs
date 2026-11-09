using AulaVirtual.Modules.Exams.Application.Practice;
using AulaVirtual.Modules.Exams.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence;

public sealed class PracticeRepository(ExamsDbContext db) : IPracticeRepository
{
    public async Task<IReadOnlyList<Exercise>> GetAllAsync(Guid lessonId, CancellationToken cancellationToken) =>
        await db.Exercises.Where(exercise => exercise.LessonId == lessonId)
            .OrderBy(exercise => exercise.Position).ThenBy(exercise => exercise.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Exercise>> GetActiveAsync(Guid lessonId, CancellationToken cancellationToken) =>
        await db.Exercises.AsNoTracking()
            .Where(exercise => exercise.LessonId == lessonId && !exercise.IsArchived)
            .OrderBy(exercise => exercise.Position).ThenBy(exercise => exercise.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Exercise?> GetAsync(Guid exerciseId, CancellationToken cancellationToken) =>
        db.Exercises.AsNoTracking().SingleOrDefaultAsync(exercise => exercise.Id == exerciseId, cancellationToken);

    public async Task<IReadOnlyList<ExerciseStatus>> GetStatusAsync(Guid studentUserId, IReadOnlyCollection<Guid> exerciseIds,
        CancellationToken cancellationToken)
    {
        if (exerciseIds.Count == 0) return [];
        var ids = exerciseIds.ToArray();
        return await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.StudentUserId == studentUserId && ids.Contains(attempt.ExerciseId))
            .GroupBy(attempt => attempt.ExerciseId)
            .Select(group => new ExerciseStatus(group.Key, group.Count(), group.Any(attempt => attempt.IsCorrect)))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAttemptsAsync(Guid studentUserId, Guid exerciseId, CancellationToken cancellationToken) =>
        db.Attempts.CountAsync(attempt => attempt.StudentUserId == studentUserId && attempt.ExerciseId == exerciseId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveAsync(IReadOnlyCollection<Guid> lessonIds,
        CancellationToken cancellationToken)
    {
        var ids = lessonIds.ToArray();
        return await db.Exercises.AsNoTracking()
            .Where(exercise => ids.Contains(exercise.LessonId) && !exercise.IsArchived)
            .GroupBy(exercise => exercise.LessonId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<(Guid StudentUserId, Guid LessonId), int>> CountSolvedAsync(
        IReadOnlyCollection<Guid> studentUserIds, IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken)
    {
        var students = studentUserIds.ToArray();
        var lessons = lessonIds.ToArray();
        // Distinct exercises answered right at least once; archived exercises do not count any more.
        var rows = await (
                from attempt in db.Attempts.AsNoTracking()
                join exercise in db.Exercises.AsNoTracking() on attempt.ExerciseId equals exercise.Id
                where students.Contains(attempt.StudentUserId) && lessons.Contains(attempt.LessonId) && attempt.IsCorrect &&
                      !exercise.IsArchived
                group attempt by new { attempt.StudentUserId, attempt.LessonId }
                into solved
                select new { solved.Key.StudentUserId, solved.Key.LessonId, Count = solved.Select(item => item.ExerciseId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => (row.StudentUserId, row.LessonId), row => row.Count);
    }

    public async Task<(IReadOnlyList<AttemptView> Items, int Total)> GetAttemptsAsync(Guid studentUserId, Guid? lessonId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var attempts = db.Attempts.AsNoTracking().Where(attempt => attempt.StudentUserId == studentUserId);
        if (lessonId is { } lesson) attempts = attempts.Where(attempt => attempt.LessonId == lesson);

        var total = await attempts.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;
        if (total == 0 || skip >= total) return ([], total);

        var items = await (
                from attempt in attempts
                join exercise in db.Exercises.AsNoTracking() on attempt.ExerciseId equals exercise.Id
                orderby attempt.AnsweredAt descending, attempt.Id
                select new AttemptView(attempt.ExerciseId, attempt.LessonId, exercise.Prompt, attempt.Answer, attempt.IsCorrect,
                    attempt.AnsweredAt))
            .Skip(skip).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public void Add(Exercise exercise) => db.Exercises.Add(exercise);

    public void Add(ExerciseAttempt attempt) => db.Attempts.Add(attempt);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
