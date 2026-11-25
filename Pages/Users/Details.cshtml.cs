using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Configuration;
using WebApp.Contracts;
using WebApp.Services;

namespace WebApp.Pages.Users;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class DetailsModel(UserAdministrationService users) : PageModel
{
    public UserResponse UserData { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await users.FindAsync(id);
        if (user is null) return NotFound();
        UserData = user;
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(string id, bool isActive)
    {
        var user = await users.FindUserAsync(id);
        if (user is null) return NotFound();
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await users.SetActiveAsync(user, isActive, actorId);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
            return RedirectToPage(new { id });
        }

        TempData["SuccessMessage"] = isActive ? "Usuario activado correctamente." : "Usuario desactivado correctamente.";
        return RedirectToPage("/Users/Index");
    }
}
