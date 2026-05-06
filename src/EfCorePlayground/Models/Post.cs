using EntityFrameworkCore.Projectables;

namespace EfCorePlayground.Models;

public class Post
{
    public int PostId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }

    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;

    public List<Tag> Tags { get; set; } = new();

    [Projectable]
    public int TagCount => Tags.Count;

    [Projectable]
    public bool IsRecent => PublishedDate >= new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
