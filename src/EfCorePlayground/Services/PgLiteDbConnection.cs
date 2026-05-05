using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using Npgsql;

namespace EfCorePlayground.Services;

/// <summary>
/// Thread-static holder for IJSRuntime so PGlite DbCommand can access it.
/// Safe in WASM since it's single-threaded.
/// </summary>
public static class PgLiteJsRuntime
{
    public static IJSRuntime? Instance { get; set; }
}

/// <summary>
/// A SQL query captured during EF Core execution, with optional result metadata.
/// </summary>
public record CapturedQuery(string Sql, int? RowCount = null, int? ColumnCount = null, int? TrackedEntities = null)
{
    /// <summary>
    /// Extracts the label from the first <c>-- comment</c> line injected by <c>TagWith()</c>.
    /// Returns null if the query has no tag.
    /// </summary>
    public string? Tag => Sql.Split('\n')
        .Select(l => l.Trim())
        .FirstOrDefault(l => l.StartsWith("--"))
        ?.TrimStart('-').Trim();
}

/// <summary>
/// Captures SQL queries executed by EF Core through PGlite.
/// Call <see cref="Start"/> before execution and <see cref="Stop"/> after to collect the SQL.
/// </summary>
public static class PgLiteSqlCapture
{
    private static List<CapturedQuery>? _captured;

    public static void Start() => _captured = new List<CapturedQuery>();

    public static void Record(string sql, int? rowCount = null, int? colCount = null)
        => _captured?.Add(new CapturedQuery(sql, rowCount, colCount));

    /// <summary>
    /// Annotates the last recorded query with the number of tracked EF Core entities.
    /// Call this right after <c>ToListAsync()</c> before the next query.
    /// </summary>
    public static void AnnotateLast(int trackedEntities)
    {
        if (_captured is not { Count: > 0 }) return;
        _captured[^1] = _captured[^1] with { TrackedEntities = trackedEntities };
    }

    public static List<CapturedQuery> Stop()
    {
        var result = _captured ?? new List<CapturedQuery>();
        _captured = null;
        return result;
    }
}

/// <summary>
/// Custom DbConnection that routes to PGlite via JavaScript interop.
/// Open/Close are no-ops that manage state; actual SQL goes through PgLiteDbCommand.
/// </summary>
public class PgLiteDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    private string _connectionString = "";

    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? "";
    }

    public override string Database => "playground";
    public override string DataSource => "pglite";
    public override string ServerVersion => "16.0";
    public override ConnectionState State => _state;

    public override void Open() => _state = ConnectionState.Open;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        _state = ConnectionState.Open;
        return Task.CompletedTask;
    }

    public override void Close() => _state = ConnectionState.Closed;

    public override void ChangeDatabase(string databaseName) { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => new PgLiteDbTransaction(this, isolationLevel);

    protected override DbCommand CreateDbCommand()
        => new PgLiteDbCommand { Connection = this };
}

/// <summary>
/// Custom DbCommand that sends SQL to PGlite via JavaScript interop.
/// </summary>
public class PgLiteDbCommand : DbCommand
{
    private string _commandText = "";
    private readonly DbParameterCollection _parameters = new PgLiteParameterCollection();

    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? "";
    }

    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override bool DesignTimeVisible { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }

    public override int ExecuteNonQuery()
    {
        return ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var js = PgLiteJsRuntime.Instance
            ?? throw new InvalidOperationException("PGlite JS runtime not initialized");

        var (sql, parameters) = PrepareForPgLite();
        var formattedSql = FormatSqlWithParams(sql, parameters);

        try
        {
            var result = await js.InvokeAsync<JsonElement>(
                "pgliteInterop.query", cancellationToken, sql, parameters);

            int affected = result.TryGetProperty("affectedRows", out var ar) ? ar.GetInt32() : 0;
            PgLiteSqlCapture.Record(formattedSql, rowCount: affected, colCount: null);
            return affected;
        }
        catch
        {
            // For DDL that can't use parameterized query, try exec
            await js.InvokeVoidAsync("pgliteInterop.exec", cancellationToken, sql);
            PgLiteSqlCapture.Record(formattedSql);
            return 0;
        }
    }

    public override object? ExecuteScalar()
    {
        return ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var reader = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken);
        if (await reader.ReadAsync(cancellationToken) && reader.FieldCount > 0)
            return reader.GetValue(0);
        return null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        return ExecuteDbDataReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var js = PgLiteJsRuntime.Instance
            ?? throw new InvalidOperationException("PGlite JS runtime not initialized");

        var (sql, parameters) = PrepareForPgLite();
        var formattedSql = FormatSqlWithParams(sql, parameters);

        var statements = SplitIntoStatements(sql, parameters);
        if (statements.Count == 1)
        {
            var result = await js.InvokeAsync<JsonElement>(
                "pgliteInterop.query", cancellationToken, statements[0].sql, statements[0].values);

            int rows = result.TryGetProperty("rows", out var rowsEl) ? rowsEl.GetArrayLength() : 0;
            int cols = result.TryGetProperty("columns", out var colsEl) ? colsEl.GetArrayLength() : 0;
            PgLiteSqlCapture.Record(formattedSql, rowCount: rows, colCount: cols);
            return PgLiteDbDataReader.FromJsonResult(result);
        }

        // Multiple statements — execute each individually and return a multi-result reader
        var results = new List<JsonElement>(statements.Count);
        int totalRows = 0, lastCols = 0;
        foreach (var (stmtSql, stmtParams) in statements)
        {
            Console.WriteLine($"[PgLite] BATCH STMT: {stmtSql}");
            var r = await js.InvokeAsync<JsonElement>(
                "pgliteInterop.query", cancellationToken, stmtSql, stmtParams);
            if (r.TryGetProperty("rows", out var re)) totalRows += re.GetArrayLength();
            if (r.TryGetProperty("columns", out var ce)) lastCols = ce.GetArrayLength();
            results.Add(r);
        }
        PgLiteSqlCapture.Record(formattedSql, rowCount: totalRows, colCount: lastCols > 0 ? lastCols : null);
        return PgLiteDbDataReader.FromJsonResults(results);
    }

    // Return NpgsqlParameter so Npgsql type mappings (e.g. NpgsqlTimestampTypeMapping)
    // pass their "parameter is NpgsqlParameter" check without throwing.
    // We still read the Value out in ExtractParameters(), so PgLite gets the raw value.
    protected override DbParameter CreateDbParameter()
        => new NpgsqlParameter();

    /// <summary>
    /// Transforms Npgsql-style named parameters ($p0, $p1, …) into PgLite positional
    /// parameters ($1, $2, …) and returns the processed SQL together with the value array.
    /// Processing in reverse index order prevents "$p1" from being matched inside "$p10".
    /// </summary>
    private static readonly Regex NamedParamRegex =
        new(@"[$@]([a-zA-Z_]\w*)", RegexOptions.Compiled);

    // Matches a positional parameter placeholder like $1, $2, $10, ...
    private static readonly Regex PositionalParamRegex =
        new(@"\$(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Splits a multi-statement SQL (joined by semicolons by EF Core's batch processor)
    /// into individual statements, re-indexing positional parameters for each sub-statement.
    /// E.g. "DELETE … WHERE id=$1; DELETE … WHERE id=$2" with values [a,b] becomes
    ///   [("DELETE … WHERE id=$1", [a]), ("DELETE … WHERE id=$1", [b])]
    /// </summary>
    private static IReadOnlyList<(string sql, object?[] values)> SplitIntoStatements(
        string sql, object?[] allValues)
    {
        // Split on semicolons; discard empty fragments (trailing semicolons etc.)
        var parts = sql.Split(';')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        if (parts.Length <= 1)
            return new[] { (sql, allValues) };

        var result = new List<(string, object?[])>(parts.Length);
        foreach (var part in parts)
        {
            // Find which $N positions this statement references (1-based, may skip numbers)
            var refs = PositionalParamRegex.Matches(part)
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (refs.Count == 0)
            {
                result.Add((part, Array.Empty<object?>()));
                continue;
            }

            // Reindex: old $N → new $k (1-based within this statement).
            // Process descending to avoid $1 matching the prefix of $10.
            var reindexed = part;
            foreach (var oldIdx in refs.OrderByDescending(n => n))
            {
                int newIdx = refs.IndexOf(oldIdx) + 1;
                // \b won't work after digits; use negative lookahead (?!\d) instead
                reindexed = Regex.Replace(reindexed, $@"\${oldIdx}(?!\d)", $"${newIdx}");
            }

            var subValues = refs
                .Select(n => n - 1 < allValues.Length ? allValues[n - 1] : null)
                .ToArray();

            result.Add((reindexed, subValues));
        }

        return result;
    }

    /// <summary>
    /// Transforms whatever parameter placeholder format Npgsql EF Core uses
    /// ($p0, @p0, etc.) into PgLite positional parameters ($1, $2, …).
    /// Scans the SQL to find named placeholders rather than guessing the prefix,
    /// so this works with any Npgsql version.
    /// </summary>
    private (string sql, object?[] values) PrepareForPgLite()
    {
        // Build name→value map from the DbParameter collection
        var paramValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var valuesInOrder = new object?[_parameters.Count];

        for (int i = 0; i < _parameters.Count; i++)
        {
            var p = (DbParameter)_parameters[i]!;
            var raw = p.Value;
            object? norm = raw switch
            {
                DBNull       => null,
                null         => null,
                DateTime dt  => dt.ToString("o"),
                DateTimeOffset dto => dto.UtcDateTime.ToString("o"),
                _            => raw
            };
            valuesInOrder[i] = norm;
            // Store under both the raw name and the name stripped of any leading @/$
            var clean = p.ParameterName.TrimStart('@', '$');
            paramValues[clean] = norm;
            if (p.ParameterName != clean)
                paramValues[p.ParameterName] = norm;
        }

        var sql = _commandText;
        var matches = NamedParamRegex.Matches(sql);

        // DEBUG — remove once confirmed working
        Console.WriteLine($"[PgLite] RAW SQL: {sql}");
        Console.WriteLine($"[PgLite] Named param matches: {matches.Count}");
        for (int i = 0; i < _parameters.Count; i++)
            Console.WriteLine($"[PgLite]   param[{i}] name='{((DbParameter)_parameters[i]!).ParameterName}' value='{valuesInOrder[i]}'");

        if (matches.Count == 0)
        {
            // SQL already uses $1, $2, … — pass values in collection order
            return (sql, valuesInOrder);
        }

        // Collect unique param names in first-appearance order → determines positional index
        var orderedNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in matches)
            if (seen.Add(m.Groups[1].Value))
                orderedNames.Add(m.Groups[1].Value);

        // Replace longest names first to avoid "$p1" matching the prefix of "$p10"
        foreach (var name in orderedNames.OrderByDescending(n => n.Length))
        {
            int pos = orderedNames.IndexOf(name) + 1; // 1-based positional index
            sql = sql.Replace($"${name}", $"${pos}");
            sql = sql.Replace($"@{name}", $"${pos}");
        }

        // Build values array in the order params first appear in SQL
        var namedValues = orderedNames
            .Select(n => paramValues.TryGetValue(n, out var v) ? v : null)
            .ToArray();

        Console.WriteLine($"[PgLite] TRANSFORMED SQL: {sql}");
        return (sql, namedValues);
    }

    /// <summary>
    /// Formats a SQL query with its parameter values substituted for display purposes.
    /// </summary>
    private static string FormatSqlWithParams(string sql, object?[] parameters)
    {
        if (parameters.Length == 0) return sql;

        var result = sql;
        // Replace parameters in reverse order to avoid $1 replacing part of $10
        for (int i = parameters.Length; i >= 1; i--)
        {
            var value = parameters[i - 1];
            var replacement = value switch
            {
                null => "NULL",
                string s => $"'{s.Replace("'", "''")}'",
                bool b => b ? "TRUE" : "FALSE",
                DateTime => $"'{value}'",
                _ => value.ToString() ?? "NULL"
            };
            result = result.Replace($"${i}", replacement);
        }
        return result;
    }
}

/// <summary>
/// Minimal DbTransaction for PGlite — mostly no-ops for EnsureCreated compatibility.
/// </summary>
public class PgLiteDbTransaction : DbTransaction
{
    public PgLiteDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
    {
        DbConnection = connection;
        IsolationLevel = isolationLevel;
    }

    protected override DbConnection? DbConnection { get; }
    public override IsolationLevel IsolationLevel { get; }
    public override void Commit() { }
    public override void Rollback() { }
}

/// <summary>
/// Simple DbParameter for PGlite commands.
/// </summary>
public class PgLiteDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; } = "";
    public override int Size { get; set; }
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }

    public override void ResetDbType() => DbType = DbType.String;
}

/// <summary>
/// Minimal DbParameterCollection for PGlite commands.
/// </summary>
public class PgLiteParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

    public override int Count => _parameters.Count;
    public override object SyncRoot => _parameters;
    public override bool IsFixedSize => false;
    public override bool IsReadOnly => false;
    public override bool IsSynchronized => false;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var v in values)
            _parameters.Add((DbParameter)v);
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);

    public override void CopyTo(Array array, int index)
        => ((System.Collections.ICollection)_parameters).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName)
        => _parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _parameters.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0) _parameters.RemoveAt(idx);
    }

    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName)
        => _parameters.First(p => p.ParameterName == parameterName);

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0) _parameters[idx] = value;
        else _parameters.Add(value);
    }
}
