using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Security;

namespace WebApp.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app, string dbPath)
    {
        app.MapGet("/api/health", () => Results.Ok(new { statusCode = 200, data = new { status = "healthy revisando" } }));

        app.MapGet("/api/database/backup", () =>
        {
            if (!File.Exists(dbPath))
                return Results.NotFound(new { statusCode = 404, data = "La base de datos no existe" });

            return Results.File(File.ReadAllBytes(dbPath), "application/octet-stream", "webapp-backup.db");
        }).RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapDelete("/api/database", async (ApplicationDbContext db) =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM tasks; DELETE FROM categories;");
            return Results.Ok(new { statusCode = 200, data = "Base de datos vaciada correctamente" });
        }).RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .RequireAntiforgeryValidation();

        return app;
    }
}
