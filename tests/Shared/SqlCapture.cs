using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AulaVirtual.Tests.Shared;

// Proves that a LINQ query translates to PostgreSQL without a running database:
// the connection is never opened and the first command is captured instead of executed.
internal sealed class SqlCapture : DbConnectionInterceptor, IDbCommandInterceptor
{
    public string? Sql { get; private set; }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(InterceptionResult.Suppress());

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default) => Capture<InterceptionResult<DbDataReader>>(command);

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default) => Capture<InterceptionResult<object>>(command);

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default) => Capture<InterceptionResult<int>>(command);

    private ValueTask<T> Capture<T>(DbCommand command)
    {
        Sql = command.CommandText;
        throw new CapturedException();
    }

    public static async Task<string> RunAsync(SqlCapture capture, Func<Task> query)
    {
        await Assert.ThrowsAsync<CapturedException>(query);
        return capture.Sql!;
    }

    public sealed class CapturedException : Exception;
}
