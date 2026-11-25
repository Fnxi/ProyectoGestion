using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using WebApp.Configuration;
using WebApp.Contracts;
using WebApp.Security;
using WebApp.Services;

namespace WebApp.Endpoints;

public static class UserAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapUserAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/admin/users").RequireAuthorization(AuthorizationPolicies.AdminOnly);

        users.MapGet("", async (UserAdministrationService service) =>
            Results.Ok(new { statusCode = 200, data = await service.ListAsync() }));

        users.MapGet("/{id}", async (string id, UserAdministrationService service) =>
        {
            var user = await service.FindAsync(id);
            return user is null
                ? Results.NotFound(new { statusCode = 404, data = "Usuario no encontrado" })
                : Results.Ok(new { statusCode = 200, data = user });
        });

        users.MapPost("", async (CreateUserRequest request, UserAdministrationService service) =>
        {
            if (!TryValidate(request, out var errors)) return Results.BadRequest(new { statusCode = 400, data = errors });
            var result = await service.CreateAsync(request);
            return result.Succeeded
                ? Results.Created($"/api/admin/users/{result.User!.Id}", new { statusCode = 201, data = result.User })
                : Results.BadRequest(new { statusCode = 400, data = result.Errors });
        }).RequireAntiforgeryValidation();

        users.MapPut("/{id}", async (string id, UpdateUserRequest request, UserAdministrationService service) =>
        {
            if (!TryValidate(request, out var errors)) return Results.BadRequest(new { statusCode = 400, data = errors });
            var user = await service.FindUserAsync(id);
            if (user is null) return Results.NotFound(new { statusCode = 404, data = "Usuario no encontrado" });
            var result = await service.UpdateAsync(user, request);
            return result.Succeeded
                ? Results.Ok(new { statusCode = 200, data = result.User })
                : Results.BadRequest(new { statusCode = 400, data = result.Errors });
        }).RequireAntiforgeryValidation();

        users.MapPut("/{id}/roles", async (string id, UpdateUserRolesRequest request, UserAdministrationService service) =>
        {
            var user = await service.FindUserAsync(id);
            if (user is null) return Results.NotFound(new { statusCode = 404, data = "Usuario no encontrado" });
            var result = await service.UpdateRolesAsync(user, request.Roles);
            return result.Succeeded
                ? Results.Ok(new { statusCode = 200, data = result.User })
                : Results.BadRequest(new { statusCode = 400, data = result.Errors });
        }).RequireAntiforgeryValidation();

        users.MapPut("/{id}/status", async (string id, UpdateUserStatusRequest request, HttpContext context, UserAdministrationService service) =>
        {
            var user = await service.FindUserAsync(id);
            if (user is null) return Results.NotFound(new { statusCode = 404, data = "Usuario no encontrado" });
            var actorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var result = await service.SetActiveAsync(user, request.IsActive, actorId);
            return result.Succeeded
                ? Results.Ok(new { statusCode = 200, data = result.User })
                : Results.BadRequest(new { statusCode = 400, data = result.Errors });
        }).RequireAntiforgeryValidation();

        return app;
    }

    private static bool TryValidate(object request, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        errors = results.SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new { member, message = result.ErrorMessage ?? "El valor no es válido." }))
            .GroupBy(error => error.member)
            .ToDictionary(group => group.Key, group => group.Select(error => error.message).ToArray());
        return valid;
    }
}
