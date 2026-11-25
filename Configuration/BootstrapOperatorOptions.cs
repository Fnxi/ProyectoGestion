namespace WebApp.Configuration;

public sealed class BootstrapOperatorOptions
{
    public const string SectionName = "BootstrapOperator";

    public string? Email { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email)
        || !string.IsNullOrWhiteSpace(UserName)
        || !string.IsNullOrWhiteSpace(Password)
        || !string.IsNullOrWhiteSpace(FirstName)
        || !string.IsNullOrWhiteSpace(LastName);
}
