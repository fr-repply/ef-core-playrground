using Microsoft.EntityFrameworkCore;
using EfCorePlayground.Models;

namespace EfCorePlayground.Models;

public class PlaygroundDbContext : DbContext
{
    public PlaygroundDbContext(DbContextOptions<PlaygroundDbContext> options) : base(options) { }

    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Post>()
            .HasMany(p => p.Tags)
            .WithMany(t => t.Posts)
            .UsingEntity("PostTag");

        SeedData(modelBuilder);
    }

    public static void SeedData(ModelBuilder modelBuilder)
    {
        var authors = new[]
        {
            new Author { AuthorId = 1, Name = "Alice Martin", Email = "alice@example.com", Bio = "Développeuse .NET passionnée" },
            new Author { AuthorId = 2, Name = "Bob Dupont", Email = "bob@example.com", Bio = "Architecte logiciel et blogueur" },
            new Author { AuthorId = 3, Name = "Claire Bernard", Email = "claire@example.com", Bio = "Experte en bases de données" },
        };

        var blogs = new[]
        {
            new Blog { BlogId = 1, Name = "Le Blog .NET", Url = "https://dotnet-blog.example.com", Rating = 5, CreatedAt = new DateTime(2023, 1, 15) },
            new Blog { BlogId = 2, Name = "Architecture Moderne", Url = "https://archi-moderne.example.com", Rating = 4, CreatedAt = new DateTime(2023, 6, 1) },
            new Blog { BlogId = 3, Name = "Data & EF Core", Url = "https://data-efcore.example.com", Rating = 5, CreatedAt = new DateTime(2024, 1, 10) },
            new Blog { BlogId = 4, Name = "Web Dev Tips", Url = "https://webdevtips.example.com", Rating = 3, CreatedAt = new DateTime(2024, 3, 20) },
        };

        var posts = new[]
        {
            new { PostId = 1, Title = "Introduction à EF Core", Content = "Entity Framework Core est un ORM moderne pour .NET...", PublishedDate = new DateTime(2023, 2, 1), BlogId = 1, AuthorId = 1 },
            new { PostId = 2, Title = "Les requêtes LINQ", Content = "LINQ permet d'écrire des requêtes typées en C#...", PublishedDate = new DateTime(2023, 3, 15), BlogId = 1, AuthorId = 1 },
            new { PostId = 3, Title = "Migrations EF Core", Content = "Les migrations permettent de versionner votre schéma...", PublishedDate = new DateTime(2023, 5, 10), BlogId = 1, AuthorId = 2 },
            new { PostId = 4, Title = "Clean Architecture avec .NET", Content = "La Clean Architecture sépare les préoccupations...", PublishedDate = new DateTime(2023, 7, 1), BlogId = 2, AuthorId = 2 },
            new { PostId = 5, Title = "CQRS et Event Sourcing", Content = "CQRS sépare les lectures des écritures...", PublishedDate = new DateTime(2023, 9, 20), BlogId = 2, AuthorId = 2 },
            new { PostId = 6, Title = "Optimiser les requêtes EF Core", Content = "Pour optimiser vos requêtes, utilisez AsNoTracking...", PublishedDate = new DateTime(2024, 1, 15), BlogId = 3, AuthorId = 3 },
            new { PostId = 7, Title = "SQLite vs PostgreSQL", Content = "Chaque base de données a ses avantages...", PublishedDate = new DateTime(2024, 2, 28), BlogId = 3, AuthorId = 3 },
            new { PostId = 8, Title = "Les index en EF Core", Content = "Les index améliorent les performances de lecture...", PublishedDate = new DateTime(2024, 4, 5), BlogId = 3, AuthorId = 1 },
            new { PostId = 9, Title = "Blazor et WASM", Content = "Blazor WebAssembly permet de créer des SPA en C#...", PublishedDate = new DateTime(2024, 5, 12), BlogId = 4, AuthorId = 1 },
            new { PostId = 10, Title = "APIs REST avec .NET", Content = "Créer des APIs REST performantes avec ASP.NET Core...", PublishedDate = new DateTime(2024, 6, 1), BlogId = 4, AuthorId = 2 },
        };

        var tags = new[]
        {
            new Tag { TagId = 1, Name = "EF Core" },
            new Tag { TagId = 2, Name = "LINQ" },
            new Tag { TagId = 3, Name = "C#" },
            new Tag { TagId = 4, Name = "Architecture" },
            new Tag { TagId = 5, Name = "Performance" },
            new Tag { TagId = 6, Name = "Database" },
            new Tag { TagId = 7, Name = "Web" },
            new Tag { TagId = 8, Name = "Blazor" },
        };

        modelBuilder.Entity<Author>().HasData(authors);
        modelBuilder.Entity<Blog>().HasData(blogs);
        modelBuilder.Entity<Post>().HasData(posts);
        modelBuilder.Entity<Tag>().HasData(tags);

        // PostTag join table
        modelBuilder.Entity("PostTag").HasData(
            new { PostsPostId = 1, TagsTagId = 1 },
            new { PostsPostId = 1, TagsTagId = 3 },
            new { PostsPostId = 2, TagsTagId = 2 },
            new { PostsPostId = 2, TagsTagId = 3 },
            new { PostsPostId = 3, TagsTagId = 1 },
            new { PostsPostId = 3, TagsTagId = 6 },
            new { PostsPostId = 4, TagsTagId = 4 },
            new { PostsPostId = 4, TagsTagId = 3 },
            new { PostsPostId = 5, TagsTagId = 4 },
            new { PostsPostId = 6, TagsTagId = 1 },
            new { PostsPostId = 6, TagsTagId = 5 },
            new { PostsPostId = 7, TagsTagId = 6 },
            new { PostsPostId = 8, TagsTagId = 1 },
            new { PostsPostId = 8, TagsTagId = 5 },
            new { PostsPostId = 8, TagsTagId = 6 },
            new { PostsPostId = 9, TagsTagId = 7 },
            new { PostsPostId = 9, TagsTagId = 8 },
            new { PostsPostId = 9, TagsTagId = 3 },
            new { PostsPostId = 10, TagsTagId = 7 },
            new { PostsPostId = 10, TagsTagId = 3 }
        );
    }
}
