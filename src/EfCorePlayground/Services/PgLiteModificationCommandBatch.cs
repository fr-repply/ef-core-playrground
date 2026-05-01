using Microsoft.EntityFrameworkCore.Update;

namespace EfCorePlayground.Services;

/// <summary>
/// Replaces NpgsqlModificationCommandBatch which does a hard cast
/// (NpgsqlConnection)connection.DbConnection — that cast fails with PgLiteDbConnection.
/// This subclass inherits AffectedCountModificationCommandBatch (EF Core Relational),
/// whose ExecuteAsync uses the standard IRelationalCommand path with no Npgsql-specific casts.
/// </summary>
public class PgLiteModificationCommandBatch : AffectedCountModificationCommandBatch
{
    public PgLiteModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies)
        : base(dependencies)
    {
    }
}

/// <summary>
/// Replaces NpgsqlModificationCommandBatchFactory with one that creates PgLiteModificationCommandBatch.
/// Registered via .ReplaceService&lt;IModificationCommandBatchFactory, PgLiteModificationCommandBatchFactory&gt;()
/// </summary>
public class PgLiteModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    private readonly ModificationCommandBatchFactoryDependencies _dependencies;

    public PgLiteModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public ModificationCommandBatch Create()
        => new PgLiteModificationCommandBatch(_dependencies);
}

