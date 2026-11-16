using AulaVirtual.Modules.Exams.Application.Evaluations;
using AulaVirtual.Modules.Exams.Domain.Evaluations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence;

public sealed class EvaluationRepository(ExamsDbContext db) : IEvaluationRepository
{
    public Task<Evaluation?> GetAsync(Guid evaluationId, CancellationToken cancellationToken) =>
        db.Evaluations.SingleOrDefaultAsync(evaluation => evaluation.Id == evaluationId, cancellationToken);

    public async Task<IReadOnlyList<Evaluation>> ListByOfferingAsync(Guid offeringId, CancellationToken cancellationToken) =>
        await db.Evaluations.AsNoTracking().Where(evaluation => evaluation.OfferingId == offeringId)
            .OrderBy(evaluation => evaluation.OpensAt).ThenBy(evaluation => evaluation.Title).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Evaluation>> ListPublishedAsync(IReadOnlyCollection<Guid> offeringIds, CancellationToken cancellationToken)
    {
        var ids = offeringIds.ToArray();
        return await db.Evaluations.AsNoTracking().Where(evaluation => evaluation.IsPublished && ids.Contains(evaluation.OfferingId))
            .OrderBy(evaluation => evaluation.ClosesAt).ThenBy(evaluation => evaluation.Title).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvaluationQuestion>> GetQuestionsAsync(Guid evaluationId, CancellationToken cancellationToken) =>
        await db.EvaluationQuestions.Where(question => question.EvaluationId == evaluationId).OrderBy(question => question.Position)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, EvaluationCounts>> CountAsync(IReadOnlyCollection<Guid> evaluationIds, CancellationToken cancellationToken)
    {
        var ids = evaluationIds.ToArray();
        var questions = await db.EvaluationQuestions.AsNoTracking().Where(question => ids.Contains(question.EvaluationId))
            .GroupBy(question => question.EvaluationId).Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Id, row => row.Count, cancellationToken);
        var attempts = await db.EvaluationAttempts.AsNoTracking().Where(attempt => ids.Contains(attempt.EvaluationId))
            .GroupBy(attempt => attempt.EvaluationId)
            .Select(group => new { Id = group.Key, Started = group.Count(), Submitted = group.Count(attempt => attempt.SubmittedAt != null) })
            .ToDictionaryAsync(row => row.Id, row => row, cancellationToken);
        return ids.ToDictionary(id => id, id => new EvaluationCounts(questions.GetValueOrDefault(id),
            attempts.TryGetValue(id, out var row) ? row.Started : 0, attempts.TryGetValue(id, out var sent) ? sent.Submitted : 0));
    }

    public async Task<IReadOnlyList<EvaluationAttempt>> GetAttemptsOfStudentAsync(Guid studentUserId, IReadOnlyCollection<Guid> evaluationIds,
        CancellationToken cancellationToken)
    {
        var ids = evaluationIds.ToArray();
        return await db.EvaluationAttempts.AsNoTracking()
            .Where(attempt => attempt.StudentUserId == studentUserId && ids.Contains(attempt.EvaluationId)).ToListAsync(cancellationToken);
    }

    public Task<EvaluationAttempt?> GetAttemptAsync(Guid evaluationId, Guid studentUserId, CancellationToken cancellationToken) =>
        db.EvaluationAttempts.AsNoTracking().SingleOrDefaultAsync(
            attempt => attempt.EvaluationId == evaluationId && attempt.StudentUserId == studentUserId, cancellationToken);

    public Task<EvaluationAttempt?> GetAttemptByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
        db.EvaluationAttempts.SingleOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);

    public async Task<IReadOnlyList<EvaluationAttempt>> GetAttemptsAsync(Guid evaluationId, CancellationToken cancellationToken) =>
        await db.EvaluationAttempts.AsNoTracking().Where(attempt => attempt.EvaluationId == evaluationId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EvaluationAnswer>> GetAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken)
    {
        if (attemptIds.Count == 0) return [];
        var ids = attemptIds.ToArray();
        return await db.EvaluationAnswers.AsNoTracking().Where(answer => ids.Contains(answer.AttemptId)).ToListAsync(cancellationToken);
    }

    public void Add(Evaluation evaluation) => db.Evaluations.Add(evaluation);

    public void Add(EvaluationQuestion question) => db.EvaluationQuestions.Add(question);

    public void Add(EvaluationAnswer answer) => db.EvaluationAnswers.Add(answer);

    public void Remove(Evaluation evaluation) => db.Evaluations.Remove(evaluation);

    public void Remove(IEnumerable<EvaluationQuestion> questions) => db.EvaluationQuestions.RemoveRange(questions);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryAddAttemptAsync(EvaluationAttempt attempt, CancellationToken cancellationToken)
    {
        db.EvaluationAttempts.Add(attempt);
        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request won the race: forget what this one tried to save.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
