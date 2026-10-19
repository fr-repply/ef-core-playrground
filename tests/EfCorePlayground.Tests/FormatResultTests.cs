using EfCorePlayground.Models;
using EfCorePlayground.Services;

namespace EfCorePlayground.Tests;

public class FormatResultTests
{
    [Fact]
    public void FormatResult_WhenListOfStrings_ShowsASingleValueColumn()
    {
        // .Select(p => p.Title).ToListAsync()
        var titles = new List<string> { "Introduction à EF Core", "Les requêtes LINQ" };

        var (output, columns, rows) = CodeExecutionService.FormatResult(titles);

        Assert.Equal("2 result(s)", output);
        Assert.Equal([CodeExecutionService.ScalarColumnName], columns!);
        Assert.Equal(["Introduction à EF Core", "Les requêtes LINQ"], rows!.Select(r => r[CodeExecutionService.ScalarColumnName]));
    }

    public static TheoryData<object, object?> ScalarCollections => new()
    {
        { new List<int> { 4, 5 }, 4 },
        { new List<decimal> { 12.5m }, 12.5m },
        { new List<bool> { true }, true },
        { new List<DateTime> { new(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc) }, new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc) },
        { new List<DateTimeOffset> { new(2024, 1, 15, 0, 0, 0, TimeSpan.Zero) }, new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero).ToString() },
        { new List<Guid> { Guid.Empty }, Guid.Empty.ToString() },
        { new List<DayOfWeek> { DayOfWeek.Monday }, DayOfWeek.Monday },
        { new List<int?> { null, 3 }, null },
    };

    [Theory]
    [MemberData(nameof(ScalarCollections))]
    public void FormatResult_WhenCollectionOfScalars_ShowsASingleValueColumn(object collection, object? expectedFirstValue)
    {
        var (_, columns, rows) = CodeExecutionService.FormatResult(collection);

        Assert.Equal([CodeExecutionService.ScalarColumnName], columns!);
        Assert.Equal(expectedFirstValue, rows![0][CodeExecutionService.ScalarColumnName]);
    }

    [Fact]
    public void FormatResult_WhenItemsExposeAnIndexer_IgnoresIt()
    {
        var items = new List<WithIndexer> { new() { Name = "Alice Martin" } };

        var (_, columns, rows) = CodeExecutionService.FormatResult(items);

        Assert.Equal(["Name"], columns!);
        Assert.Equal("Alice Martin", rows![0]["Name"]);
    }

    [Fact]
    public void FormatResult_WhenAnonymousProjection_ShowsOneColumnPerProperty()
    {
        var items = new[]
        {
            new { Blog = "Le Blog .NET", Auteur = "Alice Martin" },
            new { Blog = "Data & EF Core", Auteur = "Claire Bernard" },
        };

        var (output, columns, rows) = CodeExecutionService.FormatResult(items);

        Assert.Equal("2 result(s)", output);
        Assert.Equal(["Blog", "Auteur"], columns!);
        Assert.Equal("Claire Bernard", rows![1]["Auteur"]);
    }

    [Fact]
    public void FormatResult_WhenEntitiesWithNavigations_ShowsCollectionsAsPlaceholder()
    {
        var blogs = new List<Blog> { new() { BlogId = 1, Name = "Le Blog .NET", Rating = 5 } };

        var (_, columns, rows) = CodeExecutionService.FormatResult(blogs);

        Assert.Contains("Posts", columns!);
        Assert.Equal("[Collection]", rows![0]["Posts"]);
        Assert.Equal(5, rows[0]["Rating"]);
    }

    private sealed class WithIndexer
    {
        public string Name { get; init; } = "";

        public int this[int index] => index;
    }
}
