using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Pages.Dashboard;

[Authorize(Policy = AuthorizationPolicies.ProductManagement)]
public sealed class IndexModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public string DisplayName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string RoleSummary { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return;
        }

        DisplayName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = user.UserName ?? "Usuario";
        }

        Email = user.Email ?? string.Empty;
        RoleSummary = string.Join(", ", await userManager.GetRolesAsync(user));
    }
}
