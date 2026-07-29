using InovaGed.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace InovaGed.Infrastructure.Observability;
public static class ObservabilityExtensions
{
    public static IServiceCollection AddInovaGedObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Observability").Get<ObservabilityOptions>() ?? new();
        services.AddSingleton(options);
        services.AddSingleton<ITelemetrySanitizer, TelemetrySanitizer>();
        if (!options.Enabled) return services;
        var nodeId = configuration["Cluster:NodeId"] ?? Environment.MachineName;
        var resource = ResourceBuilder.CreateDefault().AddService(options.ServiceName, options.ServiceNamespace,
            typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString(), nodeId).AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment.name"] = options.Environment, ["inovaged.cluster.id"] = configuration["Cluster:ClusterId"] ?? "single",
            ["inovaged.cluster.mode"] = configuration["Cluster:Mode"] ?? "SingleNode", ["inovaged.node.id"] = nodeId,
            ["inovaged.node.color"] = configuration["Cluster:NodeColor"] ?? "none", ["inovaged.region"] = configuration["Cluster:Region"] ?? "unspecified",
            ["inovaged.availability_zone"] = configuration["Cluster:AvailabilityZone"] ?? "unspecified", ["inovaged.commit.sha"] = configuration["Build:CommitSha"] ?? "unknown"
        });
        var otel = services.AddOpenTelemetry().ConfigureResource(b => b.AddAttributes(resource.Build().Attributes));
        if (options.Tracing.Enabled) otel.WithTracing(t => { t.AddSource("InovaGed.Web", "InovaGed.Database", "InovaGed.Workers", "InovaGed.Ocr", "InovaGed.Preview", "InovaGed.Deployment", "InovaGed.Continuity").AddAspNetCoreInstrumentation(o => o.RecordException = options.Tracing.RecordExceptions).AddHttpClientInstrumentation().SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(Math.Clamp(options.Tracing.SamplingRatio, 0, 1)))); if (options.Otlp.Enabled && Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out var endpoint)) t.AddOtlpExporter(o => ConfigureExporter(o, endpoint, options)); });
        if (options.Metrics.Enabled) otel.WithMetrics(m => { m.AddMeter(InovaGedTelemetry.Meter.Name).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation(); if (options.Otlp.Enabled && Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out var endpoint)) m.AddOtlpExporter(o => ConfigureExporter(o, endpoint, options)); });
        return services;
    }
    private static void ConfigureExporter(OtlpExporterOptions exporter, Uri endpoint, ObservabilityOptions options)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = options.Otlp.Protocol.Equals("HttpProtobuf", StringComparison.OrdinalIgnoreCase) ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
        exporter.Headers = options.Otlp.Headers;
    }
}
