using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Pages.Products;

[Authorize(Policy = AuthorizationPolicies.ProductManagement)]
public sealed class CreateModel(ApplicationDbContext db) : PageModel
{
    [BindProperty]
    public ProductInputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var now = DateTime.UtcNow;
        var product = new Product { CreatedAt = now, UpdatedAt = now };
        Input.ApplyTo(product);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Producto creado correctamente.";
        return RedirectToPage("/Products/Index");
    }
}
