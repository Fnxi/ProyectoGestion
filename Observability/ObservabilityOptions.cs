using OpenTelemetry.Exporter;

namespace WebApp.Observability;

public sealed class ObservabilityOptions
{
    public const string DefaultServiceName = "WebApp";

    public string ServiceName { get; init; } = DefaultServiceName;
    public string EnvironmentName { get; init; } = "Production";
    public Uri? OtlpEndpoint { get; init; }
    public Uri? TracesEndpoint { get; init; }
    public Uri? MetricsEndpoint { get; init; }
    public Uri? LogsEndpoint { get; init; }
    public OtlpExportProtocol OtlpProtocol { get; init; } = OtlpExportProtocol.Grpc;
    public string? OtlpHeaders { get; init; }
    public int MetricExportIntervalMilliseconds { get; init; } = 60_000;

    public bool IsOtlpConfigured => TracesEndpoint is not null || MetricsEndpoint is not null || LogsEndpoint is not null;

    public static ObservabilityOptions FromConfiguration(IConfiguration configuration, string environmentName)
    {
        var endpoint = ParseEndpoint(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        var interval = int.TryParse(configuration["OTEL_METRIC_EXPORT_INTERVAL"], out var configuredInterval)
            ? Math.Clamp(configuredInterval, 1_000, 3_600_000)
            : 60_000;

        return new ObservabilityOptions
        {
            ServiceName = configuration["OTEL_SERVICE_NAME"]?.Trim() is { Length: > 0 } serviceName
                ? serviceName
                : DefaultServiceName,
            EnvironmentName = environmentName,
            OtlpEndpoint = endpoint,
            TracesEndpoint = ParseEndpoint(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"]) ?? endpoint,
            MetricsEndpoint = ParseEndpoint(configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"]) ?? endpoint,
            LogsEndpoint = ParseEndpoint(configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"]) ?? endpoint,
            OtlpProtocol = ParseProtocol(configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]),
            OtlpHeaders = configuration["OTEL_EXPORTER_OTLP_HEADERS"],
            MetricExportIntervalMilliseconds = interval
        };
    }

    private static OtlpExportProtocol ParseProtocol(string? protocol) => protocol?.Trim().ToLowerInvariant() switch
    {
        "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
        "grpc" or null or "" => OtlpExportProtocol.Grpc,
        _ => OtlpExportProtocol.Grpc
    };

    private static Uri? ParseEndpoint(string? endpointValue) =>
        Uri.TryCreate(endpointValue, UriKind.Absolute, out var parsedEndpoint)
        && (parsedEndpoint.Scheme == Uri.UriSchemeHttp || parsedEndpoint.Scheme == Uri.UriSchemeHttps)
            ? parsedEndpoint
            : null;
}
