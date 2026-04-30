using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EfCorePlayground.Services;

/// <summary>
/// EF Core connection interceptor that suppresses real database connection opening.
/// Since PGlite runs in-browser via JS interop, we don't need a real database connection.
/// All actual SQL execution is handled by PgLiteCommandInterceptor.
/// </summary>
public class PgLiteConnectionInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        // Suppress the real connection opening - PGlite doesn't need it
        return InterceptionResult.Suppress();
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        // Suppress the real connection opening - PGlite doesn't need it
        return new ValueTask<InterceptionResult>(InterceptionResult.Suppress());
    }

    public override void ConnectionClosed(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        // No-op - PGlite lifecycle is managed separately
    }

    public override Task ConnectionClosedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        return Task.CompletedTask;
    }
}
