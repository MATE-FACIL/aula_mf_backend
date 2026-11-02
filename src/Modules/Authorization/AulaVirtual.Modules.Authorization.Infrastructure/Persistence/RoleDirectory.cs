using AulaVirtual.Modules.Authorization.Application;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Authorization.Infrastructure.Persistence;

public sealed class RoleDirectory(AuthorizationDbContext db) : IRoleDirectory
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRoleCodesByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken token)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<string>>();
        var ids = userIds.ToArray();
        var rows = await db.UserRoles.AsNoTracking().Where(ur => ids.Contains(ur.UserId))
            .Join(db.Roles, ur => ur.RoleId, role => role.Id, (ur, role) => new { ur.UserId, role.Code })
            .ToListAsync(token);
        return rows.GroupBy(row => row.UserId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<string>)group.Select(row => row.Code).Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleCode, CancellationToken token) =>
        await db.UserRoles.AsNoTracking()
            .Join(db.Roles, ur => ur.RoleId, role => role.Id, (ur, role) => new { ur.UserId, role.Code })
            .Where(row => row.Code == roleCode).Select(row => row.UserId).Distinct().ToListAsync(token);

    public async Task<IReadOnlyDictionary<string, int>> CountUsersByRoleAsync(CancellationToken token) =>
        await db.UserRoles.AsNoTracking()
            .Join(db.Roles, ur => ur.RoleId, role => role.Id, (_, role) => role.Code)
            .GroupBy(code => code).Select(group => new { Code = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Code, row => row.Count, token);
}
