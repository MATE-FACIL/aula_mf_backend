namespace AulaVirtual.Modules.Authorization.Application;

// Cross-module read contract: role codes only, resolved in batches to avoid one query per user.
public interface IRoleDirectory
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken cancellationToken);
}
