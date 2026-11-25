using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Configuration;
using WebApp.Contracts;
using WebApp.Services;

namespace WebApp.Pages.Users;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class CreateModel(UserAdministrationService users) : PageModel
{
    [BindProperty]
    public CreateUserRequest Input { get; set; } = new();
    public IReadOnlyList<string> Roles => UserAdministrationService.AllowedRoles;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var result = await users.CreateAsync(Input);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        TempData["SuccessMessage"] = "Usuario creado correctamente.";
        return RedirectToPage("/Users/Index");
    }
}
