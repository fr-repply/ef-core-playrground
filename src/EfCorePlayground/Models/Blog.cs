using EntityFrameworkCore.Projectables;

namespace EfCorePlayground.Models;

public class Blog
{
    public int BlogId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Post> Posts { get; set; } = new();

    [Projectable]
    public int PostCount => Posts.Count;

    [Projectable]
    public bool IsPopular => Rating >= 4;
}
