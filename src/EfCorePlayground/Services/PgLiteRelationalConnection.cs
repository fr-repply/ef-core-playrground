using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EfCorePlayground.Services;

/// <summary>
/// Custom IRelationalConnection that uses PgLiteDbConnection instead of NpgsqlConnection.
/// </summary>
public class PgLiteRelationalConnection : RelationalConnection
{
    public PgLiteRelationalConnection(RelationalConnectionDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override DbConnection CreateDbConnection()
        => new PgLiteDbConnection();
}
