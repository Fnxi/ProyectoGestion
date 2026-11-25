using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Configuration;
using WebApp.Contracts;
using WebApp.Services;

namespace WebApp.Pages.Users;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class IndexModel(UserAdministrationService users) : PageModel
{
    public IReadOnlyList<UserResponse> Users { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Users = await users.ListAsync();
    }
}
