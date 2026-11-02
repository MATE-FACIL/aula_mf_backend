using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence;

public sealed class UserDirectory(UsersDbContext dbContext) : IUserDirectory
{
    private const int MaxSearchTerms = 5;

    public async Task<UserSearchPage> SearchAsync(
        UserSearchQuery query,
        CancellationToken cancellationToken)
    {
        var users = dbContext.Users.AsNoTracking();

        if (query.RestrictToIds is not null)
        {
            if (query.RestrictToIds.Count == 0)
            {
                return new UserSearchPage([], 0);
            }

            var ids = query.RestrictToIds.ToArray();
            users = users.Where(user => ids.Contains(user.Id));
        }

        foreach (var term in SplitTerms(query.Text))
        {
            // Names compare without accents and case; email and document only without case.
            var pattern = $"%{EscapeLike(term)}%";
            users = users.Where(user =>
                EF.Functions.ILike(EF.Functions.Unaccent(user.GivenNames), EF.Functions.Unaccent(pattern), "\\") ||
                EF.Functions.ILike(EF.Functions.Unaccent(user.FirstSurname), EF.Functions.Unaccent(pattern), "\\") ||
                (user.SecondSurname != null &&
                    EF.Functions.ILike(EF.Functions.Unaccent(user.SecondSurname), EF.Functions.Unaccent(pattern), "\\")) ||
                (user.Email != null && EF.Functions.ILike(user.Email, pattern, "\\")) ||
                (user.DocumentNumber != null && EF.Functions.ILike(user.DocumentNumber, pattern, "\\")));
        }

        var total = await users.CountAsync(cancellationToken);
        var skip = (query.Page - 1) * query.PageSize;

        if (total == 0 || skip >= total)
        {
            return new UserSearchPage([], total);
        }

        var items = await Project(users
                .OrderByDescending(user => user.CreatedAt)
                .ThenBy(user => user.Id)
                .Skip(skip)
                .Take(query.PageSize))
            .ToListAsync(cancellationToken);

        return new UserSearchPage(items, total);
    }

    public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Project(dbContext.Users.AsNoTracking().Where(user => user.Id == userId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<UserSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var ids = userIds.ToArray();
        return await Project(dbContext.Users.AsNoTracking()
                .Where(user => ids.Contains(user.Id))
                .OrderBy(user => user.GivenNames)
                .ThenBy(user => user.FirstSurname))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) =>
        createdSince is null
            ? dbContext.Users.CountAsync(cancellationToken)
            : dbContext.Users.CountAsync(user => user.CreatedAt >= createdSince, cancellationToken);

    // Single SQL statement: the link count is a correlated subquery, not one query per row.
    private IQueryable<UserSummary> Project(IQueryable<User> users) =>
        users.Select(user => new UserSummary(
            user.Id,
            user.GivenNames,
            user.FirstSurname,
            user.SecondSurname,
            user.Email,
            user.DocumentType,
            user.DocumentNumber,
            user.CreatedAt,
            dbContext.GuardianLinks.Count(link =>
                link.GuardianUserId == user.Id || link.StudentUserId == user.Id)));

    private static IEnumerable<string> SplitTerms(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(term => term.Length > 100 ? term[..100] : term)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSearchTerms);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
