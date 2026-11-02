using AulaVirtual.Modules.Auth.Application.Accounts.ChangeStatus;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Auth.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Auth.Tests;

public sealed class AccountAdministrationTests
{
    private readonly SqlCapture capture = new();

    private AccountAdministration Administration() => new(new AuthDbContext(
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model")
            .AddInterceptors(capture)
            .Options));

    [Fact]
    public async Task AccountsForAPage_AreLoadedWithTheirLoginsInSql()
    {
        var sql = await SqlCapture.RunAsync(capture, () =>
            Administration().GetByUserIdsAsync([Guid.NewGuid(), Guid.NewGuid()], default));

        Assert.Contains("= ANY (", sql);
        Assert.Contains("auth_identifiers", sql);
    }

    [Fact]
    public async Task StatusQueries_TranslateToSql()
    {
        var byStatus = await SqlCapture.RunAsync(capture, () =>
            Administration().GetUserIdsByStatusAsync(AccountStatus.Inactive, default));
        var forChange = await SqlCapture.RunAsync(capture, () =>
            Administration().FindForStatusChangeAsync(Guid.NewGuid(), default));

        Assert.Contains("status", byStatus);
        Assert.Contains("user_id", forChange);
    }

    [Fact]
    public async Task Deactivation_RevokesSessions_AndIsIdempotent()
    {
        var accounts = new InMemoryAccounts();
        var account = AuthAccount.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", DateTimeOffset.UtcNow);
        var session = account.StartSession(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        accounts.Items.Add(account);
        var handler = new ChangeAccountStatusHandler(accounts, TimeProvider.System);

        Assert.Equal(ChangeAccountStatusResult.Changed, await handler.ExecuteAsync(account.UserId, false, default));
        Assert.Equal(ChangeAccountStatusResult.Unchanged, await handler.ExecuteAsync(account.UserId, false, default));
        Assert.Equal(ChangeAccountStatusResult.NotFound, await handler.ExecuteAsync(Guid.NewGuid(), true, default));
        Assert.Equal(AccountStatus.Inactive, account.Status);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal(1, accounts.SaveChangesCalls);

        Assert.Equal(ChangeAccountStatusResult.Changed, await handler.ExecuteAsync(account.UserId, true, default));
        Assert.Equal(AccountStatus.Active, account.Status);
    }

    private sealed class InMemoryAccounts : Application.Abstractions.IAccountAdministration
    {
        public List<AuthAccount> Items { get; } = [];
        public int SaveChangesCalls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, Application.Abstractions.AccountSummary>> GetByUserIdsAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetUserIdsByStatusAsync(AccountStatus status,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthAccount?> FindForStatusChangeAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(account => account.UserId == userId));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }
}
