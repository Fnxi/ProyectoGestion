using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Pages.Products;

[Authorize(Policy = AuthorizationPolicies.ProductManagement)]
public sealed class DetailsModel(ApplicationDbContext db) : PageModel
{
    public Product Product { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == id);
        if (product is null) return NotFound();

        Product = product;
        return Page();
    }
}
