using Microsoft.EntityFrameworkCore;
using WebApp.Contracts;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Models;
using WebApp.Security;

namespace WebApp.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/categories")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        categories.MapGet("", async (ApplicationDbContext db) =>
        {
            var data = await db.Categories.AsNoTracking().OrderBy(category => category.Id)
                .Select(category => new { id = category.Id, name = category.Name })
                .ToListAsync();
            return Results.Ok(new { statusCode = 200, data });
        });

        categories.MapPost("", async (CreateCategory request, ApplicationDbContext db) =>
        {
            var category = new Category { Name = request.Name };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = new { id = category.Id, message = "Categoría creada correctamente" } });
        }).RequireAntiforgeryValidation();

        categories.MapDelete("/{id:int}", async (int id, ApplicationDbContext db) =>
        {
            var category = await db.Categories.SingleOrDefaultAsync(category => category.Id == id);
            if (category is null)
                return Results.NotFound(new { statusCode = 404, data = "Categoría no encontrada" });

            db.Categories.Remove(category);
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = "Categoría eliminada correctamente" });
        }).RequireAntiforgeryValidation();

        categories.MapGet("/{id:int}/tasks", async (int id, ApplicationDbContext db) =>
        {
            if (!await db.Categories.AnyAsync(category => category.Id == id))
                return Results.NotFound(new { statusCode = 404, data = "Categoría no encontrada" });

            var data = await db.Tasks.AsNoTracking().Where(task => task.CategoryId == id).OrderBy(task => task.Id)
                .Select(task => new { id = task.Id, title = task.Title, completed = task.Completed })
                .ToListAsync();
            return Results.Ok(new { statusCode = 200, data });
        });

        return app;
    }
}
