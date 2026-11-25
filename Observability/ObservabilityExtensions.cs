using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WebApp.Observability;

public static class ObservabilityExtensions
{
    public static ObservabilityOptions AddWebAppObservability(this WebApplicationBuilder builder)
    {
        var options = ObservabilityOptions.FromConfiguration(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddSingleton(options);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", options.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing.AddSource(WebAppTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(instrumentation => instrumentation.RecordException = true)
                    .AddHttpClientInstrumentation(instrumentation => instrumentation.RecordException = true)
                    .AddEntityFrameworkCoreInstrumentation();

                if (options.TracesEndpoint is not null)
                {
                    tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, options.TracesEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(WebAppTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (options.MetricsEndpoint is not null)
                {
                    metrics.AddOtlpExporter((exporter, reader) =>
                    {
                        ConfigureExporter(exporter, options, options.MetricsEndpoint);
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.MetricExportIntervalMilliseconds;
                    });
                }
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            if (options.LogsEndpoint is not null)
            {
                logging.AddOtlpExporter(exporter => ConfigureExporter(exporter, options, options.LogsEndpoint));
            }
        });

        return options;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, ObservabilityOptions options, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = options.OtlpProtocol;
        exporter.Headers = options.OtlpHeaders;
    }
}
