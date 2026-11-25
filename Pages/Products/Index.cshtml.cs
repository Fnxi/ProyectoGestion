using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Pages.Products;

[Authorize(Policy = AuthorizationPolicies.ProductManagement)]
public sealed class IndexModel(ApplicationDbContext db) : PageModel
{
    public IReadOnlyList<Product> Products { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Products = await db.Products.AsNoTracking()
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .ToListAsync();
    }
}
