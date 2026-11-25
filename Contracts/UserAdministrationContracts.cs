using System.ComponentModel.DataAnnotations;

namespace WebApp.Contracts;

public sealed class CreateUserRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;
}

public sealed class UpdateUserRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }
}

public sealed class UpdateUserRolesRequest
{
    [Required, MinLength(1)]
    public List<string> Roles { get; set; } = [];
}

public sealed class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}

public sealed record UserResponse(
    string Id,
    string UserName,
    string Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTime CreatedAtUtc);
