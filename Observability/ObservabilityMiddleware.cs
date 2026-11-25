using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Routing;

namespace WebApp.Observability;

public sealed class ObservabilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var route = GetRoute(context);
        var method = context.Request.Method;
        var stopwatch = Stopwatch.StartNew();
        using var activity = WebAppTelemetry.ActivitySource.StartActivity("webapp.http.request", ActivityKind.Internal);
        activity?.SetTag("http.request.method", method);
        activity?.SetTag("http.route", route);

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            activity?.SetTag("error.type", exception.GetType().Name);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var tags = new TagList
            {
                { "http.request.method", method },
                { "http.route", route },
                { "http.response.status_code", statusCode }
            };
            WebAppTelemetry.HttpRequestCounter.Add(1, tags);
            WebAppTelemetry.HttpDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            if (statusCode >= 400)
            {
                WebAppTelemetry.HttpErrorCounter.Add(1, tags);
            }
            else
            {
                WebAppTelemetry.HttpSuccessCounter.Add(1, tags);
            }

            activity?.SetTag("http.response.status_code", statusCode);
            if (statusCode >= 500) activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    private static string GetRoute(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
        ?? context.GetEndpoint()?.DisplayName
        ?? "unmatched";
}
