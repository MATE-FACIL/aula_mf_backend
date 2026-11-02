using AulaVirtual.Modules.Auth.Application.Abstractions;
using AulaVirtual.Modules.Auth.Domain.Accounts;

namespace AulaVirtual.Modules.Auth.Application.Accounts.ChangeStatus;

public enum ChangeAccountStatusResult
{
    Changed,
    Unchanged,
    NotFound
}

public sealed class ChangeAccountStatusHandler(
    IAccountAdministration accounts,
    TimeProvider timeProvider)
{
    // Deactivating revokes every session: refresh stops working at once and /auth/me
    // reports «inactive», so the frontend ends the session on its next request.
    public async Task<ChangeAccountStatusResult> ExecuteAsync(
        Guid userId,
        bool active,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindForStatusChangeAsync(userId, cancellationToken);

        if (account is null)
        {
            return ChangeAccountStatusResult.NotFound;
        }

        var isActive = account.Status == AccountStatus.Active;

        if (isActive == active)
        {
            return ChangeAccountStatusResult.Unchanged;
        }

        var now = timeProvider.GetUtcNow();

        if (active)
        {
            account.Activate(now);
        }
        else
        {
            account.Deactivate(now);
        }

        await accounts.SaveChangesAsync(cancellationToken);
        return ChangeAccountStatusResult.Changed;
    }
}
