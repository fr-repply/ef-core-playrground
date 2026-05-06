using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EfCorePlayground.Services;

/// <summary>
/// Custom database creator for PGlite.
/// PGlite is always available (in-memory), so Exists() is always true.
/// HasTables() is always false for a fresh instance, triggering CreateTables().
/// </summary>
public class PgLiteDatabaseCreator : RelationalDatabaseCreator
{
    public PgLiteDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>PGlite is always available (in-memory PostgreSQL).</summary>
    public override bool Exists() => true;

    /// <summary>PGlite is always available (in-memory PostgreSQL).</summary>
    public override Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <summary>
    /// Returns false for a fresh PGlite instance, triggering EnsureCreated to call CreateTables().
    /// </summary>
    public override bool HasTables() => false;

    /// <summary>
    /// Returns false for a fresh PGlite instance, triggering EnsureCreated to call CreateTables().
    /// </summary>
    public override Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>No-op — PGlite database is created in-memory via JS.</summary>
    public override void Create() { }

    /// <summary>No-op — PGlite database is created in-memory via JS.</summary>
    public override Task CreateAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>No-op — PGlite lifecycle is managed via JS interop.</summary>
    public override void Delete() { }

    /// <summary>No-op — PGlite lifecycle is managed via JS interop.</summary>
    public override Task DeleteAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
