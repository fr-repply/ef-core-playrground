using System.Text.Json;
using EfCorePlayground.Services;
using Microsoft.JSInterop;
using Npgsql;

namespace EfCorePlayground.Tests;

public class PgLiteDbCommandTests
{
    private static PgLiteDbCommand CreateCommand(string sql, params object?[] values)
    {
        var command = new PgLiteDbCommand { CommandText = sql };
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters.Add(new NpgsqlParameter { ParameterName = $"@p{i}", Value = values[i] ?? DBNull.Value });
        }

        return command;
    }

    // ── SplitIntoStatements ─────────────────────────────────────────────

    [Fact]
    public void SplitIntoStatements_WhenSecondStatementUsesParameters4To7_RenumbersWithoutCascade()
    {
        // Batch EF Core sends when inserting a graph (new Author + new Blog, then its Posts)
        const string sql =
            "INSERT INTO \"Authors\" (\"Bio\", \"Email\", \"Name\") VALUES ($1, $2, $3) RETURNING \"AuthorId\";\n" +
            "INSERT INTO \"Blogs\" (\"CreatedAt\", \"Name\", \"Rating\", \"Url\") VALUES ($4, $5, $6, $7) RETURNING \"BlogId\";";
        object?[] values = ["Bio", "zoe@example.com", "Zoé", "2026-10-19T00:00:00.0000000Z", "Nouveau blog", 4, "https://nouveau.example.com"];

        var statements = PgLiteDbCommand.SplitIntoStatements(sql, values);

        Assert.Equal(2, statements.Count);
        Assert.Equal("INSERT INTO \"Authors\" (\"Bio\", \"Email\", \"Name\") VALUES ($1, $2, $3) RETURNING \"AuthorId\"", statements[0].sql);
        Assert.Equal(new object?[] { "Bio", "zoe@example.com", "Zoé" }, statements[0].values);
        Assert.Equal("INSERT INTO \"Blogs\" (\"CreatedAt\", \"Name\", \"Rating\", \"Url\") VALUES ($1, $2, $3, $4) RETURNING \"BlogId\"", statements[1].sql);
        Assert.Equal(new object?[] { "2026-10-19T00:00:00.0000000Z", "Nouveau blog", 4, "https://nouveau.example.com" }, statements[1].values);
    }

    [Fact]
    public void SplitIntoStatements_WhenIndexesHaveSeveralDigits_DoesNotConfuseDollar1WithDollar10()
    {
        const string sql = "UPDATE t SET a = $1 WHERE id = $2; UPDATE t SET a = $10, b = $11 WHERE id = $12";
        var values = Enumerable.Range(1, 12).Cast<object?>().ToArray();

        var statements = PgLiteDbCommand.SplitIntoStatements(sql, values);

        Assert.Equal("UPDATE t SET a = $1 WHERE id = $2", statements[0].sql);
        Assert.Equal(new object?[] { 1, 2 }, statements[0].values);
        Assert.Equal("UPDATE t SET a = $1, b = $2 WHERE id = $3", statements[1].sql);
        Assert.Equal(new object?[] { 10, 11, 12 }, statements[1].values);
    }

    [Fact]
    public void SplitIntoStatements_WhenSingleStatement_ReturnsItUnchanged()
    {
        object?[] values = [42];

        var statements = PgLiteDbCommand.SplitIntoStatements("SELECT * FROM t WHERE id = $1", values);

        var statement = Assert.Single(statements);
        Assert.Equal("SELECT * FROM t WHERE id = $1", statement.sql);
        Assert.Same(values, statement.values);
    }

    // ── Full command path, through a fake JS runtime ────────────────────

    [Fact]
    public async Task ExecuteReader_WhenInsertingAGraph_SendsConsistentlyNumberedStatementsToPgLite()
    {
        var jsRuntime = new RecordingJsRuntime();
        PgLiteJsRuntime.Instance = jsRuntime;
        var createdAt = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var command = CreateCommand(
            "INSERT INTO \"Authors\" (\"Bio\", \"Email\", \"Name\") VALUES (@p0, @p1, @p2) RETURNING \"AuthorId\";\n" +
            "INSERT INTO \"Blogs\" (\"CreatedAt\", \"Name\", \"Rating\", \"Url\") VALUES (@p3, @p4, @p5, @p6) RETURNING \"BlogId\";",
            "Bio", "zoe@example.com", "Zoé", createdAt, "Nouveau blog", 4, "https://nouveau.example.com");

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(2, jsRuntime.Queries.Count);
        Assert.Equal("INSERT INTO \"Blogs\" (\"CreatedAt\", \"Name\", \"Rating\", \"Url\") VALUES ($1, $2, $3, $4) RETURNING \"BlogId\"", jsRuntime.Queries[1].Sql);
        Assert.Equal(new object?[] { createdAt.ToString("o"), "Nouveau blog", 4, "https://nouveau.example.com" }, jsRuntime.Queries[1].Values);
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Sql, object?[] Values)> Queries { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("pgliteInterop.query", identifier);
            Queries.Add(((string)args![0]!, (object?[])args[1]!));
            var result = JsonDocument.Parse("""{ "columns": ["Id"], "rows": [[1]], "affectedRows": 1 }""").RootElement;
            return ValueTask.FromResult((TValue)(object)result);
        }
    }
}
