using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Pages.Users;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class EditModel(UserAdministrationService users) : PageModel
{
    [BindProperty]
    public UserEditInputModel Input { get; set; } = new();
    public IReadOnlyList<string> Roles => UserAdministrationService.AllowedRoles;

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await users.FindAsync(id);
        if (user is null) return NotFound();
        Input = UserEditInputModel.FromUser(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        var user = await users.FindUserAsync(id);
        if (user is null) return NotFound();
        if (!ModelState.IsValid) return Page();

        var profileResult = await users.UpdateAsync(user, Input.ToRequest());
        if (!profileResult.Succeeded)
        {
            foreach (var error in profileResult.Errors) ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        var rolesResult = await users.UpdateRolesAsync(user, Input.Roles);
        if (!rolesResult.Succeeded)
        {
            foreach (var error in rolesResult.Errors) ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
        return RedirectToPage("/Users/Index");
    }
}
