using System.Text.Json;
using Microsoft.JSInterop;
using EfCorePlayground.Services;

namespace EfCorePlayground;

public static class PlaygroundInterop
{
    private static readonly CodeExecutionService _executionService = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [JSInvokable]
    public static async Task<string> ExecuteCode(string code)
    {
        var result = await _executionService.ExecuteAsync(code);
        return JsonSerializer.Serialize(result, _jsonOptions);
    }

    [JSInvokable]
    public static Task<string> GetDefaultCode()
    {
        var code = """
            // Bienvenue dans le Playground EF Core !
            // Écrivez vos requêtes LINQ ci-dessous.
            // La variable 'db' est votre DbContext avec les tables:
            //   - db.Blogs (BlogId, Name, Url, Rating, CreatedAt)
            //   - db.Posts (PostId, Title, Content, PublishedDate, BlogId, AuthorId)
            //   - db.Authors (AuthorId, Name, Email, Bio)
            //   - db.Tags (TagId, Name)
            //
            // Exemples:
            //   return await db.Blogs.ToListAsync();
            //   return await db.Posts.Where(p => p.BlogId == 1).ToListAsync();
            //   return await db.Blogs.Where(b => b.Rating >= 4).Select(b => new { b.Name, b.Rating }).ToListAsync();

            return await db.Blogs.ToListAsync();
            """;
        return Task.FromResult(code);
    }

    [JSInvokable]
    public static Task<string> GetExamples()
    {
        var examples = new[]
        {
            new
            {
                Title = "Lister tous les blogs",
                Code = "return await db.Blogs.ToListAsync();"
            },
            new
            {
                Title = "Blogs avec rating >= 4",
                Code = "return await db.Blogs\n    .Where(b => b.Rating >= 4)\n    .OrderByDescending(b => b.Rating)\n    .ToListAsync();"
            },
            new
            {
                Title = "Posts avec leur auteur",
                Code = "return await db.Posts\n    .Include(p => p.Author)\n    .Select(p => new { p.Title, Author = p.Author.Name, p.PublishedDate })\n    .ToListAsync();"
            },
            new
            {
                Title = "Nombre de posts par blog",
                Code = "return await db.Blogs\n    .Select(b => new { Blog = b.Name, PostCount = b.Posts.Count() })\n    .OrderByDescending(x => x.PostCount)\n    .ToListAsync();"
            },
            new
            {
                Title = "Posts avec tags (Many-to-Many)",
                Code = "return await db.Posts\n    .Include(p => p.Tags)\n    .Select(p => new { p.Title, Tags = string.Join(\", \", p.Tags.Select(t => t.Name)) })\n    .ToListAsync();"
            },
            new
            {
                Title = "Recherche par mot-clé",
                Code = "return await db.Posts\n    .Where(p => p.Title.Contains(\"EF Core\"))\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();"
            },
            new
            {
                Title = "Groupement par auteur",
                Code = "return await db.Authors\n    .Select(a => new {\n        Author = a.Name,\n        PostCount = a.Posts.Count(),\n        Blogs = string.Join(\", \", a.Posts.Select(p => p.Blog.Name).Distinct())\n    })\n    .ToListAsync();"
            },
            new
            {
                Title = "Top 5 posts récents",
                Code = "return await db.Posts\n    .OrderByDescending(p => p.PublishedDate)\n    .Take(5)\n    .Select(p => new { p.Title, p.PublishedDate, Blog = p.Blog.Name })\n    .ToListAsync();"
            }
        };

        return Task.FromResult(JsonSerializer.Serialize(examples, _jsonOptions));
    }

    [JSInvokable]
    public static Task<string> GetSchemaInfo()
    {
        var schema = new
        {
            Tables = new[]
            {
                new
                {
                    Name = "Blogs",
                    Columns = new[]
                    {
                        new { Name = "BlogId", Type = "int", IsPrimaryKey = true },
                        new { Name = "Name", Type = "string", IsPrimaryKey = false },
                        new { Name = "Url", Type = "string", IsPrimaryKey = false },
                        new { Name = "Rating", Type = "int", IsPrimaryKey = false },
                        new { Name = "CreatedAt", Type = "DateTime", IsPrimaryKey = false },
                    }
                },
                new
                {
                    Name = "Posts",
                    Columns = new[]
                    {
                        new { Name = "PostId", Type = "int", IsPrimaryKey = true },
                        new { Name = "Title", Type = "string", IsPrimaryKey = false },
                        new { Name = "Content", Type = "string", IsPrimaryKey = false },
                        new { Name = "PublishedDate", Type = "DateTime", IsPrimaryKey = false },
                        new { Name = "BlogId", Type = "int (FK)", IsPrimaryKey = false },
                        new { Name = "AuthorId", Type = "int (FK)", IsPrimaryKey = false },
                    }
                },
                new
                {
                    Name = "Authors",
                    Columns = new[]
                    {
                        new { Name = "AuthorId", Type = "int", IsPrimaryKey = true },
                        new { Name = "Name", Type = "string", IsPrimaryKey = false },
                        new { Name = "Email", Type = "string", IsPrimaryKey = false },
                        new { Name = "Bio", Type = "string", IsPrimaryKey = false },
                    }
                },
                new
                {
                    Name = "Tags",
                    Columns = new[]
                    {
                        new { Name = "TagId", Type = "int", IsPrimaryKey = true },
                        new { Name = "Name", Type = "string", IsPrimaryKey = false },
                    }
                },
            },
            Relationships = new[]
            {
                "Blog 1──* Post",
                "Author 1──* Post",
                "Post *──* Tag (via PostTag)",
            }
        };

        return Task.FromResult(JsonSerializer.Serialize(schema, _jsonOptions));
    }
}
