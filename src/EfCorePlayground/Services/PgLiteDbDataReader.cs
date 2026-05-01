using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace EfCorePlayground.Services;

/// <summary>
/// A DbDataReader that wraps PGlite query results returned from JavaScript interop.
/// Supports multiple result sets (NextResult) for batched modification commands.
/// </summary>
public class PgLiteDbDataReader : DbDataReader
{
    private record ResultSet(string[] Columns, JsonElement[][] Rows, int AffectedRows);

    private readonly List<ResultSet> _resultSets;
    private int _currentResultSetIndex = 0;
    private int _currentRow = -1;
    private bool _closed;

    private ResultSet Current => _resultSets[_currentResultSetIndex];
    private string[] Columns => Current.Columns;
    private JsonElement[][] Rows => Current.Rows;

    public PgLiteDbDataReader(string[] columns, JsonElement[][] rows, int affectedRows = -1)
    {
        _resultSets = new List<ResultSet> { new(columns, rows, affectedRows) };
    }

    private PgLiteDbDataReader(List<ResultSet> resultSets)
    {
        _resultSets = resultSets;
    }

    /// <summary>
    /// Create a PgLiteDbDataReader from a single JS interop result.
    /// </summary>
    public static PgLiteDbDataReader FromJsonResult(JsonElement result)
        => new PgLiteDbDataReader(new List<ResultSet> { ParseResult(result) });

    /// <summary>
    /// Create a PgLiteDbDataReader from multiple JS interop results (for batched statements).
    /// </summary>
    public static PgLiteDbDataReader FromJsonResults(IEnumerable<JsonElement> results)
        => new PgLiteDbDataReader(results.Select(ParseResult).ToList());

    private static ResultSet ParseResult(JsonElement result)
    {
        var columns = Array.Empty<string>();
        var rows = Array.Empty<JsonElement[]>();
        var affectedRows = 0;

        if (result.TryGetProperty("columns", out var colsEl))
            columns = colsEl.EnumerateArray().Select(c => c.GetString() ?? "").ToArray();

        if (result.TryGetProperty("rows", out var rowsEl))
            rows = rowsEl.EnumerateArray()
                .Select(row => row.EnumerateArray().ToArray())
                .ToArray();

        if (result.TryGetProperty("affectedRows", out var arEl))
            affectedRows = arEl.GetInt32();

        return new ResultSet(columns, rows, affectedRows);
    }

    public override int FieldCount => Columns.Length;
    public override bool HasRows => Rows.Length > 0;
    public override int RecordsAffected => Current.AffectedRows;
    public override bool IsClosed => _closed;
    public override int Depth => 0;

    public override bool Read()
    {
        _currentRow++;
        return _currentRow < Rows.Length;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Read());

    public override bool NextResult()
    {
        if (_currentResultSetIndex < _resultSets.Count - 1)
        {
            _currentResultSetIndex++;
            _currentRow = -1;
            return true;
        }
        return false;
    }

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        => Task.FromResult(NextResult());

    public override string GetName(int ordinal) => Columns[ordinal];

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < Columns.Length; i++)
        {
            if (string.Equals(Columns[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new IndexOutOfRangeException($"Column '{name}' not found. Available: {string.Join(", ", Columns)}");
    }

    public override bool IsDBNull(int ordinal)
    {
        var el = Rows[_currentRow][ordinal];
        return el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined;
    }

    public override object GetValue(int ordinal)
    {
        if (IsDBNull(ordinal))
            return DBNull.Value;

        var el = Rows[_currentRow][ordinal];
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()!,
            // Prefer int32 so EF Core can unbox to int without InvalidCastException.
            // (boxing a long and then casting to int throws at runtime)
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => el.GetRawText()
        };
    }

    public override T GetFieldValue<T>(int ordinal)
    {
        if (IsDBNull(ordinal))
            return default!;

        var el = Rows[_currentRow][ordinal];

        // Unwrap Nullable<T> so callers using Nullable<int> etc. work too
        var targetType = typeof(T);
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null)
        {
            // Recursively call with the underlying type via reflection to avoid duplicate code
            var method = typeof(PgLiteDbDataReader)
                .GetMethod(nameof(GetFieldValue))!
                .MakeGenericMethod(underlying);
            return (T)method.Invoke(this, new object[] { ordinal })!;
        }

        if (targetType == typeof(int))    return (T)(object)el.GetInt32();
        if (targetType == typeof(long))   return (T)(object)el.GetInt64();
        if (targetType == typeof(short))  return (T)(object)(short)el.GetInt32();
        if (targetType == typeof(byte))   return (T)(object)(byte)el.GetInt32();
        if (targetType == typeof(string))
            return (T)(object)(el.ValueKind == JsonValueKind.String ? el.GetString()! : el.GetRawText());
        if (targetType == typeof(bool))   return (T)(object)el.GetBoolean();
        if (targetType == typeof(double)) return (T)(object)el.GetDouble();
        if (targetType == typeof(float))  return (T)(object)(float)el.GetDouble();
        if (targetType == typeof(decimal)) return (T)(object)el.GetDecimal();
        if (targetType == typeof(DateTime))
        {
            var s = el.ValueKind == JsonValueKind.String ? el.GetString()! : null;
            return (T)(object)(s != null ? DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind) : el.GetDateTime());
        }
        if (targetType == typeof(DateTimeOffset))
        {
            var s = el.ValueKind == JsonValueKind.String ? el.GetString()! : null;
            return (T)(object)(s != null ? DateTimeOffset.Parse(s) : el.GetDateTimeOffset());
        }
        if (targetType == typeof(Guid))
        {
            return (T)(object)(el.ValueKind == JsonValueKind.String
                ? Guid.Parse(el.GetString()!)
                : el.GetGuid());
        }
        if (targetType == typeof(byte[]))  return (T)(object)el.GetBytesFromBase64();
        if (targetType == typeof(object))  return (T)GetValue(ordinal);

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
