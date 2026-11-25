using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Data;

namespace WebApp.Pages.Products;

[Authorize(Policy = AuthorizationPolicies.ProductManagement)]
public sealed class EditModel(ApplicationDbContext db) : PageModel
{
    [BindProperty]
    public ProductInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == id);
        if (product is null) return NotFound();

        Input = ProductInputModel.FromProduct(product);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid) return Page();

        var product = await db.Products.SingleOrDefaultAsync(product => product.Id == id);
        if (product is null) return NotFound();

        Input.ApplyTo(product);
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Producto actualizado correctamente.";
        return RedirectToPage("/Products/Index");
    }
}
