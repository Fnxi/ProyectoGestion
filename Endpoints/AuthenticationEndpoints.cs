using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using WebApp.Contracts;
using WebApp.Models;
using WebApp.Security;

namespace WebApp.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        });

        auth.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("WebApp.Authentication");
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                logger.LogWarning("Authentication failed due to invalid credentials.");
                return Results.Unauthorized();
            }

            var user = await userManager.FindByEmailAsync(request.Email.Trim());
            if (user is null || !user.IsActive)
            {
                // Se devuelve el mismo mensaje para no revelar si un correo existe.
                logger.LogWarning("Authentication failed due to invalid credentials.");
                return Results.Unauthorized();
            }

            var result = await signInManager.PasswordSignInAsync(
                user.UserName!, request.Password, isPersistent: false, lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                logger.LogWarning("Authentication failed due to invalid credentials.");
                return Results.Unauthorized();
            }

            logger.LogInformation("Authentication succeeded.");
            return Results.Redirect("/dashboard");
        }).RequireAntiforgeryValidation();

        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/account/login");
        }).RequireAuthorization().RequireAntiforgeryValidation();

        return app;
    }
}
