using WebApp.Configuration;

namespace WebApp.Endpoints;

public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        // Rutas transitorias de verificación de políticas. No exponen datos ni CRUD;
        // serán reemplazadas por los módulos reales en las siguientes etapas.
        app.MapGet("/api/products/authorization", () => Results.NoContent())
            .RequireAuthorization(AuthorizationPolicies.ProductManagement);

        app.MapGet("/api/admin/users/authorization", () => Results.NoContent())
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        return app;
    }
}
