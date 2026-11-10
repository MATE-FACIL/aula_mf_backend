using AulaVirtual.Modules.Exams.Application.Diagnostics;
using AulaVirtual.Modules.Exams.Domain.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence;

public sealed class DiagnosticRepository(ExamsDbContext db) : IDiagnosticRepository
{
    public async Task<IReadOnlyList<DiagnosticQuestion>> GetAllQuestionsAsync(string stage, CancellationToken cancellationToken) =>
        await db.DiagnosticQuestions.Where(question => question.Stage == stage)
            .OrderBy(question => question.Position).ThenBy(question => question.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiagnosticQuestion>> GetActiveQuestionsAsync(string stage, CancellationToken cancellationToken) =>
        await db.DiagnosticQuestions.AsNoTracking()
            .Where(question => question.Stage == stage && !question.IsArchived)
            .OrderBy(question => question.Position).ThenBy(question => question.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiagnosticQuestion>> GetQuestionsAsync(IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0) return [];
        var ids = questionIds.ToArray();
        return await db.DiagnosticQuestions.AsNoTracking().Where(question => ids.Contains(question.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountActiveQuestionsAsync(CancellationToken cancellationToken) =>
        await db.DiagnosticQuestions.AsNoTracking()
            .Where(question => !question.IsArchived)
            .GroupBy(question => question.Stage)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

    public async Task<IReadOnlyList<DiagnosticAttempt>> GetAttemptsOfAsync(Guid studentUserId, CancellationToken cancellationToken) =>
        await db.DiagnosticAttempts.AsNoTracking().Where(attempt => attempt.StudentUserId == studentUserId).ToListAsync(cancellationToken);

    public Task<DiagnosticAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
        db.DiagnosticAttempts.SingleOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);

    public async Task<IReadOnlyList<DiagnosticAnswer>> GetAnswersAsync(IReadOnlyCollection<Guid> attemptIds,
        CancellationToken cancellationToken)
    {
        if (attemptIds.Count == 0) return [];
        var ids = attemptIds.ToArray();
        return await db.DiagnosticAnswers.AsNoTracking().Where(answer => ids.Contains(answer.AttemptId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountAnswersAsync(IReadOnlyCollection<Guid> attemptIds,
        CancellationToken cancellationToken)
    {
        if (attemptIds.Count == 0) return new Dictionary<Guid, int>();
        var ids = attemptIds.ToArray();
        return await db.DiagnosticAnswers.AsNoTracking()
            .Where(answer => ids.Contains(answer.AttemptId))
            .GroupBy(answer => answer.AttemptId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
    }

    public async Task<(IReadOnlyList<DiagnosticAttempt> Items, int Total)> SearchAttemptsAsync(DiagnosticFilter filter, string? stage,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var attempts = db.DiagnosticAttempts.AsNoTracking();
        attempts = filter switch
        {
            DiagnosticFilter.Pending => attempts.Where(a => a.Status == DiagnosticStatus.Completed && a.ConfirmedLevel == null),
            DiagnosticFilter.Confirmed => attempts.Where(a => a.ConfirmedLevel != null),
            DiagnosticFilter.InProgress => attempts.Where(a => a.Status == DiagnosticStatus.InProgress),
            _ => attempts
        };
        if (stage is not null) attempts = attempts.Where(a => a.Stage == stage);

        var total = await attempts.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;
        if (total == 0 || skip >= total) return ([], total);
        var items = await attempts.OrderByDescending(a => a.StartedAt).ThenBy(a => a.Id).Skip(skip).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public void Add(DiagnosticQuestion question) => db.DiagnosticQuestions.Add(question);

    public void Add(DiagnosticAttempt attempt) => db.DiagnosticAttempts.Add(attempt);

    public void Add(DiagnosticAnswer answer) => db.DiagnosticAnswers.Add(answer);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
