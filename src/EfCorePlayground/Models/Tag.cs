namespace EfCorePlayground.Models;

public class Tag
{
    public int TagId { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<Post> Posts { get; set; } = new();
}
