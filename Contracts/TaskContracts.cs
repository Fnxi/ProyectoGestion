namespace WebApp.Contracts;

public sealed record CreateTask(string Title, bool Completed, int CategoryId);
public sealed record UpdateTask(string Title, bool Completed, int CategoryId);
public sealed record CreateCategory(string Name);
