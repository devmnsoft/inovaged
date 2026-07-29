namespace InovaGed.Infrastructure.Observability;
public sealed class ObservabilityOptions
{
    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "InovaGed.Web";
    public string ServiceNamespace { get; set; } = "InovaGed";
    public string Environment { get; set; } = "Production";
    public OtlpOptions Otlp { get; set; } = new();
    public TracingOptions Tracing { get; set; } = new();
    public MetricsOptions Metrics { get; set; } = new();
}
public sealed class OtlpOptions { public bool Enabled { get; set; } public string Endpoint { get; set; } = ""; public string Protocol { get; set; } = "Grpc"; public string Headers { get; set; } = ""; }
public sealed class TracingOptions { public bool Enabled { get; set; } = true; public double SamplingRatio { get; set; } = .1; public bool RecordExceptions { get; set; } = true; }
public sealed class MetricsOptions { public bool Enabled { get; set; } = true; public bool PrometheusEndpointEnabled { get; set; } }
