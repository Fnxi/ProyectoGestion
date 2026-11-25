namespace WebApp.Models;

public sealed class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}
