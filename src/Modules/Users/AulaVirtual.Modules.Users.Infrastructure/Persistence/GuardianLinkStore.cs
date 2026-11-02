using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Domain.Guardianship;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence;

public sealed class GuardianLinkStore(UsersDbContext dbContext, TimeProvider timeProvider) : IGuardianLinks
{
    public async Task<bool> LinkAsync(
        Guid guardianUserId,
        Guid studentUserId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GuardianLinks.AnyAsync(
                link => link.GuardianUserId == guardianUserId && link.StudentUserId == studentUserId,
                cancellationToken))
        {
            return false;
        }

        var link = GuardianLink.Create(guardianUserId, studentUserId, timeProvider.GetUtcNow());
        dbContext.GuardianLinks.Add(link);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request created the same link in the meantime: the end state is the same.
            dbContext.Entry(link).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> UnlinkAsync(
        Guid guardianUserId,
        Guid studentUserId,
        CancellationToken cancellationToken)
    {
        var deleted = await dbContext.GuardianLinks
            .Where(link => link.GuardianUserId == guardianUserId && link.StudentUserId == studentUserId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public async Task<IReadOnlyList<Guid>> GetStudentIdsAsync(
        Guid guardianUserId,
        CancellationToken cancellationToken) =>
        await dbContext.GuardianLinks.AsNoTracking()
            .Where(link => link.GuardianUserId == guardianUserId)
            .Select(link => link.StudentUserId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetGuardianIdsAsync(
        Guid studentUserId,
        CancellationToken cancellationToken) =>
        await dbContext.GuardianLinks.AsNoTracking()
            .Where(link => link.StudentUserId == studentUserId)
            .Select(link => link.GuardianUserId)
            .ToListAsync(cancellationToken);
}
