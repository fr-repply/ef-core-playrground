using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.JSInterop;

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

        var sql = _commandText;
        var parameters = ExtractParameters();

        try
        {
            var result = await js.InvokeAsync<JsonElement>(
                "pgliteInterop.query", cancellationToken, sql, parameters);

            if (result.TryGetProperty("affectedRows", out var ar))
                return ar.GetInt32();
            return 0;
        }
        catch
        {
            // For DDL that can't use parameterized query, try exec
            await js.InvokeVoidAsync("pgliteInterop.exec", cancellationToken, sql);
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

        var sql = _commandText;
        var parameters = ExtractParameters();

        var result = await js.InvokeAsync<JsonElement>(
            "pgliteInterop.query", cancellationToken, sql, parameters);

        return PgLiteDbDataReader.FromJsonResult(result);
    }

    protected override DbParameter CreateDbParameter()
        => new PgLiteDbParameter();

    private object?[] ExtractParameters()
    {
        var result = new object?[_parameters.Count];
        for (int i = 0; i < _parameters.Count; i++)
        {
            var p = (DbParameter)_parameters[i]!;
            var value = p.Value;
            if (value == DBNull.Value) value = null;
            if (value is DateTime dt) value = dt.ToString("o");
            result[i] = value;
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
