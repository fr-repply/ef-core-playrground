using EntityFrameworkCore.Projectables;

namespace EfCorePlayground.Models;

public class Author
{
    public int AuthorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;

    public List<Post> Posts { get; set; } = new();

    [Projectable]
    public int PostCount => Posts.Count;

    [Projectable]
    public bool IsProductive => Posts.Count >= 3;
}
