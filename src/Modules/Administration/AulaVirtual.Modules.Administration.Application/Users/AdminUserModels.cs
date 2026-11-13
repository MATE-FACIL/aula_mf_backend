namespace AulaVirtual.Modules.Administration.Application.Users;

public static class AdminAccountStatus
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    // The person exists in Users but has no sign-in account yet.
    public const string NoAccount = "no_account";
}

public sealed record AdminUserListQuery(
    string? Search,
    string? Role,
    string? Status,
    int Page = 1,
    int PageSize = AdminUserListQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record AdminUserListItem(
    Guid Id,
    string FullName,
    string? Email,
    string? DocumentNumber,
    string? LoginIdentifier,
    IReadOnlyList<string> Roles,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    int LinkCount);

public sealed record AdminLinkedUser(Guid Id, string FullName, string? LoginIdentifier, string Status);

public static class AdminConsentStatus
{
    public const string Accepted = "accepted";
    // A guardian is linked but has not accepted the current text yet.
    public const string Pending = "pending";
    // Nobody can give the consent: the student has no guardian linked.
    public const string NoGuardian = "no_guardian";
}

/// <summary>Where the parent's consent for a student's data stands (SCRUM-38), for the current text.</summary>
public sealed record AdminConsent(
    string Status,
    string CurrentVersion,
    bool IsProvisional,
    DateTimeOffset? AcceptedAt,
    string? AcceptedByName);

public sealed record AdminUserDetail(
    Guid Id,
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string FullName,
    string? Email,
    string? DocumentType,
    string? DocumentNumber,
    string? LoginIdentifier,
    IReadOnlyList<string> Roles,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<AdminLinkedUser> Students,
    IReadOnlyList<AdminLinkedUser> Guardians,
    /// <summary>Only for students; null for anyone else.</summary>
    AdminConsent? Consent);

public sealed record AdminSummary(
    int TotalUsers,
    int NewUsersLast7Days,
    IReadOnlyDictionary<string, int> UsersByRole,
    IReadOnlyList<AdminUserListItem> RecentUsers);

public sealed record CreateStudentInput(
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string? Email,
    string DocumentType,
    string DocumentNumber,
    Guid? GuardianUserId);

public sealed record CreateGuardianInput(
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string Email,
    string? DocumentType,
    string? DocumentNumber);

/// <summary>The initial password is returned only once; only its hash is stored.</summary>
public sealed record IssuedCredentials(Guid UserId, string LoginIdentifier, string InitialPassword);

public enum AdminResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

public sealed record AdminResult<T>(
    AdminResultStatus Status,
    T? Value = default,
    string? Detail = null,
    IDictionary<string, string[]>? Errors = null)
{
    public static AdminResult<T> Ok(T value) => new(AdminResultStatus.Ok, value);

    public static AdminResult<T> NotFound(string detail) => new(AdminResultStatus.NotFound, Detail: detail);

    public static AdminResult<T> Invalid(string detail, IDictionary<string, string[]>? errors = null) =>
        new(AdminResultStatus.Invalid, Detail: detail, Errors: errors);

    public static AdminResult<T> Conflict(string detail) => new(AdminResultStatus.Conflict, Detail: detail);
}
