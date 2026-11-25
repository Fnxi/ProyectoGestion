using Microsoft.EntityFrameworkCore;
using WebApp.Contracts;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Models;
using WebApp.Security;

namespace WebApp.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/tasks")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        tasks.MapGet("", async (ApplicationDbContext db) =>
        {
            var data = await db.Tasks.AsNoTracking().OrderBy(task => task.Id)
                .Select(task => new { id = task.Id, title = task.Title, completed = task.Completed, categoryId = task.CategoryId })
                .ToListAsync();
            return Results.Ok(new { statusCode = 200, data });
        });

        tasks.MapGet("/{id:int}", async (int id, ApplicationDbContext db) =>
        {
            var task = await db.Tasks.AsNoTracking().Where(task => task.Id == id)
                .Select(task => new { id = task.Id, title = task.Title, completed = task.Completed, categoryId = task.CategoryId })
                .SingleOrDefaultAsync();
            return task is null
                ? Results.NotFound(new { statusCode = 404, data = "Tarea no encontrada" })
                : Results.Ok(new { statusCode = 200, data = task });
        });

        tasks.MapPost("", async (CreateTask request, ApplicationDbContext db) =>
        {
            if (!await db.Categories.AnyAsync(category => category.Id == request.CategoryId))
                return Results.BadRequest(new { statusCode = 400, data = "La categoría no existe" });

            var task = new TaskItem { Title = request.Title, Completed = request.Completed, CategoryId = request.CategoryId };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = new { id = task.Id, message = "Tarea creada correctamente" } });
        }).RequireAntiforgeryValidation();

        tasks.MapPut("/{id:int}", async (int id, UpdateTask request, ApplicationDbContext db) =>
        {
            if (!await db.Categories.AnyAsync(category => category.Id == request.CategoryId))
                return Results.BadRequest(new { statusCode = 400, data = "La categoría no existe" });

            var task = await db.Tasks.SingleOrDefaultAsync(task => task.Id == id);
            if (task is null)
                return Results.NotFound(new { statusCode = 404, data = "Tarea no encontrada" });

            task.Title = request.Title;
            task.Completed = request.Completed;
            task.CategoryId = request.CategoryId;
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = "Tarea actualizada correctamente" });
        }).RequireAntiforgeryValidation();

        tasks.MapDelete("/{id:int}", async (int id, ApplicationDbContext db) =>
        {
            var task = await db.Tasks.SingleOrDefaultAsync(task => task.Id == id);
            if (task is null)
                return Results.NotFound(new { statusCode = 404, data = "Tarea no encontrada" });

            db.Tasks.Remove(task);
            await db.SaveChangesAsync();
            return Results.Ok(new { statusCode = 200, data = "Tarea eliminada correctamente" });
        }).RequireAntiforgeryValidation();

        return app;
    }
}
