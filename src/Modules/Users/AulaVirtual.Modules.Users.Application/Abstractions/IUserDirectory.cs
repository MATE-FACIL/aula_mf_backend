namespace AulaVirtual.Modules.Users.Application.Abstractions;

public sealed record UserSummary(
    Guid Id,
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string? Email,
    string? DocumentType,
    string? DocumentNumber,
    DateTimeOffset CreatedAt,
    int LinkCount)
{
    public string FullName => SecondSurname is null
        ? $"{GivenNames} {FirstSurname}"
        : $"{GivenNames} {FirstSurname} {SecondSurname}";
}

/// <param name="Text">Every word must match a name, surname, email or document (case-insensitive).</param>
/// <param name="RestrictToIds">When set, only these users are considered (e.g. users holding a role).</param>
public sealed record UserSearchQuery(
    string? Text,
    IReadOnlyCollection<Guid>? RestrictToIds,
    int Page,
    int PageSize);

public sealed record UserSearchPage(IReadOnlyList<UserSummary> Items, int Total);

// Cross-module read contract for back-office screens. Results are newest first and paged in the database.
public interface IUserDirectory
{
    Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken);

    Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken);
}
