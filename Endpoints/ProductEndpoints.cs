using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Contracts;
using WebApp.Data;
using WebApp.Models;
using WebApp.Security;

namespace WebApp.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products")
            .RequireAuthorization(AuthorizationPolicies.ProductManagement);

        products.MapGet("", async (ApplicationDbContext db) =>
        {
            var data = await db.Products.AsNoTracking()
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id)
                .Select(product => ToResponse(product))
                .ToListAsync();
            return Results.Ok(new { statusCode = 200, data });
        });

        products.MapGet("/{id:int}", async (int id, ApplicationDbContext db) =>
        {
            var product = await db.Products.AsNoTracking()
                .Where(product => product.Id == id)
                .Select(product => ToResponse(product))
                .SingleOrDefaultAsync();

            return product is null
                ? Results.NotFound(new { statusCode = 404, data = "Producto no encontrado" })
                : Results.Ok(new { statusCode = 200, data = product });
        });

        products.MapPost("", async (ProductRequest request, ApplicationDbContext db) =>
        {
            if (TryValidate(request, out var errors))
            {
                return Results.BadRequest(new { statusCode = 400, data = errors });
            }

            var now = DateTime.UtcNow;
            var product = new Product
            {
                Name = request.Name.Trim(),
                Description = NormalizeDescription(request.Description),
                Price = request.Price,
                Stock = request.Stock,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return Results.Created($"/api/products/{product.Id}", new { statusCode = 201, data = ToResponse(product) });
        }).RequireAntiforgeryValidation();

        products.MapPut("/{id:int}", async (int id, ProductRequest request, ApplicationDbContext db) =>
        {
            if (TryValidate(request, out var errors))
            {
                return Results.BadRequest(new { statusCode = 400, data = errors });
            }

            var product = await db.Products.SingleOrDefaultAsync(product => product.Id == id);
            if (product is null)
            {
                return Results.NotFound(new { statusCode = 404, data = "Producto no encontrado" });
            }

            product.Name = request.Name.Trim();
            product.Description = NormalizeDescription(request.Description);
            product.Price = request.Price;
            product.Stock = request.Stock;
            product.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = ToResponse(product) });
        }).RequireAntiforgeryValidation();

        products.MapDelete("/{id:int}", async (int id, ApplicationDbContext db) =>
        {
            var product = await db.Products.SingleOrDefaultAsync(product => product.Id == id);
            if (product is null)
            {
                return Results.NotFound(new { statusCode = 404, data = "Producto no encontrado" });
            }

            db.Products.Remove(product);
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = "Producto eliminado correctamente" });
        }).RequireAntiforgeryValidation();

        return app;
    }

    private static ProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Price,
        product.Stock,
        DateTime.SpecifyKind(product.CreatedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(product.UpdatedAt, DateTimeKind.Utc));

    private static bool TryValidate(ProductRequest request, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        errors = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new { member, message = result.ErrorMessage ?? "El valor no es válido." }))
            .GroupBy(error => error.member)
            .ToDictionary(group => group.Key, group => group.Select(error => error.message).ToArray());
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["Name"] = ["El nombre es obligatorio."];
            isValid = false;
        }
        return !isValid;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
