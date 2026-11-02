namespace AulaVirtual.Modules.Users.Application.Abstractions;

// Parent/guardian ↔ student links. Operations are idempotent: linking twice or unlinking a
// missing link is not an error, the boolean only tells whether something changed.
public interface IGuardianLinks
{
    Task<bool> LinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken);

    Task<bool> UnlinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetStudentIdsAsync(Guid guardianUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetGuardianIdsAsync(Guid studentUserId, CancellationToken cancellationToken);
}
