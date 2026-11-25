using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WebApp.Observability;

public static class WebAppTelemetry
{
    public const string ActivitySourceName = "WebApp.Application";
    public const string MeterName = "WebApp.Observability";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> HttpRequestCounter = Meter.CreateCounter<long>(
        "webapp.http.server.requests", unit: "{request}", description: "Cantidad de solicitudes HTTP recibidas.");
    public static readonly Counter<long> HttpErrorCounter = Meter.CreateCounter<long>(
        "webapp.http.server.errors", unit: "{error}", description: "Cantidad de solicitudes HTTP fallidas.");
    public static readonly Counter<long> HttpSuccessCounter = Meter.CreateCounter<long>(
        "webapp.http.server.successes", unit: "{request}", description: "Cantidad de solicitudes HTTP exitosas.");
    public static readonly Histogram<double> HttpDuration = Meter.CreateHistogram<double>(
        "webapp.http.server.duration", unit: "ms", description: "Duración de solicitudes HTTP en milisegundos.");
}
