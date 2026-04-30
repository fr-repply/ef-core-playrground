using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.JSInterop;

namespace EfCorePlayground.Services;

/// <summary>
/// EF Core command interceptor that redirects all SQL execution to PGlite via JavaScript interop.
/// Instead of executing against a real PostgreSQL server, all commands are sent to PGlite
/// running as WASM in the browser.
/// </summary>
public class PgLiteCommandInterceptor : DbCommandInterceptor
{
    private readonly IJSRuntime _jsRuntime;

    public PgLiteCommandInterceptor(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        return ExecuteViaPgLiteReaderAsync(command, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        // Sync calls are not supported in Blazor WASM JS interop, but EF Core mostly uses async
        var task = ExecuteViaPgLiteReaderAsync(command, CancellationToken.None);
        return task.AsTask().GetAwaiter().GetResult();
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        return ExecuteViaPgLiteNonQueryAsync(command, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        var task = ExecuteViaPgLiteNonQueryAsync(command, CancellationToken.None);
        return task.AsTask().GetAwaiter().GetResult();
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        return ExecuteViaPgLiteScalarAsync(command, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        var task = ExecuteViaPgLiteScalarAsync(command, CancellationToken.None);
        return task.AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask<InterceptionResult<DbDataReader>> ExecuteViaPgLiteReaderAsync(
        DbCommand command, CancellationToken cancellationToken)
    {
        var sql = command.CommandText;
        var parameters = ExtractParameters(command);

        var jsonResult = await _jsRuntime.InvokeAsync<JsonElement>(
            "pgliteInterop.query", cancellationToken, sql, parameters);

        var reader = PgLiteDbDataReader.FromJsonResult(jsonResult);
        return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
    }

    private async ValueTask<InterceptionResult<int>> ExecuteViaPgLiteNonQueryAsync(
        DbCommand command, CancellationToken cancellationToken)
    {
        var sql = command.CommandText;
        var parameters = ExtractParameters(command);

        try
        {
            var jsonResult = await _jsRuntime.InvokeAsync<JsonElement>(
                "pgliteInterop.query", cancellationToken, sql, parameters);

            var affectedRows = 0;
            if (jsonResult.TryGetProperty("affectedRows", out var ar))
                affectedRows = ar.GetInt32();

            return InterceptionResult<int>.SuppressWithResult(affectedRows);
        }
        catch
        {
            // For DDL statements that don't return results, use exec
            await _jsRuntime.InvokeVoidAsync("pgliteInterop.exec", cancellationToken, sql);
            return InterceptionResult<int>.SuppressWithResult(0);
        }
    }

    private async ValueTask<InterceptionResult<object>> ExecuteViaPgLiteScalarAsync(
        DbCommand command, CancellationToken cancellationToken)
    {
        var sql = command.CommandText;
        var parameters = ExtractParameters(command);

        var jsonResult = await _jsRuntime.InvokeAsync<JsonElement>(
            "pgliteInterop.query", cancellationToken, sql, parameters);

        object? scalarValue = null;

        if (jsonResult.TryGetProperty("rows", out var rowsEl))
        {
            var rows = rowsEl.EnumerateArray().ToArray();
            if (rows.Length > 0)
            {
                var firstRow = rows[0].EnumerateArray().ToArray();
                if (firstRow.Length > 0)
                {
                    var el = firstRow[0];
                    scalarValue = el.ValueKind switch
                    {
                        JsonValueKind.String => el.GetString(),
                        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => DBNull.Value,
                        _ => el.GetRawText()
                    };
                }
            }
        }

        return InterceptionResult<object>.SuppressWithResult(scalarValue ?? DBNull.Value);
    }

    private static object?[] ExtractParameters(DbCommand command)
    {
        var parameters = new object?[command.Parameters.Count];
        for (int i = 0; i < command.Parameters.Count; i++)
        {
            var p = command.Parameters[i];
            var value = p.Value;

            // Convert DBNull to null for JSON serialization
            if (value == DBNull.Value)
                value = null;

            // Convert DateTime to ISO string for PGlite
            if (value is DateTime dt)
                value = dt.ToString("o");

            parameters[i] = value;
        }
        return parameters;
    }
}
