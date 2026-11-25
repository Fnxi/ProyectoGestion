using Microsoft.AspNetCore.Antiforgery;

namespace WebApp.Security;

public static class AntiforgeryEndpointExtensions
{
    public static RouteHandlerBuilder RequireAntiforgeryValidation(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { statusCode = 400, data = "Solicitud no válida" });
            }

            return await next(context);
        });
    }
}
