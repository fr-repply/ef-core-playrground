using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace EfCorePlayground.Services;

/// <summary>
/// A DbDataReader that wraps PGlite query results returned from JavaScript interop.
/// Provides the interface EF Core needs to materialize query results.
/// </summary>
public class PgLiteDbDataReader : DbDataReader
{
    private readonly string[] _columns;
    private readonly JsonElement[][] _rows;
    private readonly int _affectedRows;
    private int _currentRow = -1;
    private bool _closed;

    public PgLiteDbDataReader(string[] columns, JsonElement[][] rows, int affectedRows = -1)
    {
        _columns = columns;
        _rows = rows;
        _affectedRows = affectedRows;
    }

    /// <summary>
    /// Create a PgLiteDbDataReader from a JS interop result (JsonElement with columns, rows, affectedRows).
    /// </summary>
    public static PgLiteDbDataReader FromJsonResult(JsonElement result)
    {
        var columns = Array.Empty<string>();
        var rows = Array.Empty<JsonElement[]>();
        var affectedRows = 0;

        if (result.TryGetProperty("columns", out var colsEl))
        {
            columns = colsEl.EnumerateArray()
                .Select(c => c.GetString() ?? "")
                .ToArray();
        }

        if (result.TryGetProperty("rows", out var rowsEl))
        {
            rows = rowsEl.EnumerateArray()
                .Select(row => row.EnumerateArray().ToArray())
                .ToArray();
        }

        if (result.TryGetProperty("affectedRows", out var arEl))
        {
            affectedRows = arEl.GetInt32();
        }

        return new PgLiteDbDataReader(columns, rows, affectedRows);
    }

    public override int FieldCount => _columns.Length;
    public override bool HasRows => _rows.Length > 0;
    public override int RecordsAffected => _affectedRows;
    public override bool IsClosed => _closed;
    public override int Depth => 0;

    public override bool Read()
    {
        _currentRow++;
        return _currentRow < _rows.Length;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Read());

    public override bool NextResult() => false;

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public override string GetName(int ordinal) => _columns[ordinal];

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (string.Equals(_columns[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new IndexOutOfRangeException($"Column '{name}' not found. Available: {string.Join(", ", _columns)}");
    }

    public override bool IsDBNull(int ordinal)
    {
        var el = _rows[_currentRow][ordinal];
        return el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined;
    }

    public override object GetValue(int ordinal)
    {
        if (IsDBNull(ordinal))
            return DBNull.Value;

        var el = _rows[_currentRow][ordinal];
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()!,
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => el.GetRawText()
        };
    }

    public override T GetFieldValue<T>(int ordinal)
    {
        if (IsDBNull(ordinal))
            return default!;

        var el = _rows[_currentRow][ordinal];

        if (typeof(T) == typeof(int))
            return (T)(object)el.GetInt32();
        if (typeof(T) == typeof(long))
            return (T)(object)el.GetInt64();
        if (typeof(T) == typeof(short))
            return (T)(object)(short)el.GetInt32();
        if (typeof(T) == typeof(byte))
            return (T)(object)(byte)el.GetInt32();
        if (typeof(T) == typeof(string))
            return (T)(object)(el.ValueKind == JsonValueKind.String ? el.GetString()! : el.GetRawText());
        if (typeof(T) == typeof(bool))
            return (T)(object)el.GetBoolean();
        if (typeof(T) == typeof(double))
            return (T)(object)el.GetDouble();
        if (typeof(T) == typeof(float))
            return (T)(object)(float)el.GetDouble();
        if (typeof(T) == typeof(decimal))
            return (T)(object)el.GetDecimal();
        if (typeof(T) == typeof(DateTime))
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString()!;
                return (T)(object)DateTime.Parse(s);
            }
            return (T)(object)el.GetDateTime();
        }
        if (typeof(T) == typeof(DateTimeOffset))
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString()!;
                return (T)(object)DateTimeOffset.Parse(s);
            }
            return (T)(object)el.GetDateTimeOffset();
        }
        if (typeof(T) == typeof(Guid))
        {
            if (el.ValueKind == JsonValueKind.String)
                return (T)(object)Guid.Parse(el.GetString()!);
            return (T)(object)el.GetGuid();
        }
        if (typeof(T) == typeof(byte[]))
            return (T)(object)el.GetBytesFromBase64();

        return el.Deserialize<T>()!;
    }

    // Required DbDataReader overrides
    public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);
    public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);
    public override char GetChar(int ordinal) => GetFieldValue<string>(ordinal)[0];
    public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);
    public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);
    public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);
    public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);
    public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);
    public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);
    public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);
    public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);
    public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override string GetDataTypeName(int ordinal) => "text";
    public override Type GetFieldType(int ordinal) => typeof(object);

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
            values[i] = GetValue(i);
        return count;
    }

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override void Close() => _closed = true;

    public override DataTable GetSchemaTable() => new();

    public override System.Collections.IEnumerator GetEnumerator()
        => throw new NotSupportedException();
}
