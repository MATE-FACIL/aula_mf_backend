namespace AulaVirtual.Modules.Auth.Api.Contracts;

// Roles and DisplayName let the frontend route the session (student, guardian, administrator)
// from server data instead of trusting which login form was used.
public sealed record CurrentAccountResponse(
    Guid AuthAccountId,
    Guid UserId,
    IReadOnlyList<string> LoginTypes,
    string Status,
    IReadOnlyList<string> Roles,
    string? DisplayName);
