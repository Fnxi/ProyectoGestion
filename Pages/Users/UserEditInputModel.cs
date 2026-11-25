using System.ComponentModel.DataAnnotations;
using WebApp.Contracts;

namespace WebApp.Pages.Users;

public sealed class UserEditInputModel
{
    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "Introduce un correo electrónico válido.")]
    [Display(Name = "Correo electrónico")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
    [StringLength(256)]
    [Display(Name = "Nombre de usuario")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Nombre")]
    public string? FirstName { get; set; }

    [StringLength(100)]
    [Display(Name = "Apellido")]
    public string? LastName { get; set; }

    [MinLength(1, ErrorMessage = "Debe seleccionarse al menos un rol.")]
    public List<string> Roles { get; set; } = [];

    public UpdateUserRequest ToRequest() => new()
    {
        Email = Email,
        UserName = UserName,
        FirstName = FirstName,
        LastName = LastName
    };

    public static UserEditInputModel FromUser(UserResponse user) => new()
    {
        Email = user.Email,
        UserName = user.UserName,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Roles = user.Roles.ToList()
    };
}
