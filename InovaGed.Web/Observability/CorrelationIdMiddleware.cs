using System.Diagnostics;
using System.Diagnostics.Metrics;
using InovaGed.Application.Observability;

namespace InovaGed.Web.Observability;
public interface ICorrelationContext { string? CorrelationId { get; set; } }
public sealed class CorrelationContext : ICorrelationContext { public string? CorrelationId { get; set; } }
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var id = Guid.TryParseExact(supplied, "D", out var parsed) ? parsed.ToString("D") : Guid.NewGuid().ToString("D");
        correlation.CorrelationId = id;
        context.Response.Headers[HeaderName] = id;
        Activity.Current?.SetTag("inovaged.correlation_id", id);
        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = id, ["TraceId"] = Activity.Current?.TraceId.ToString(), ["SpanId"] = Activity.Current?.SpanId.ToString() }))
            await next(context);
    }
}
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var value = accessor.HttpContext?.RequestServices.GetService<ICorrelationContext>()?.CorrelationId;
        if (Guid.TryParse(value, out _)) request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, value);
        return base.SendAsync(request, cancellationToken);
    }
}
public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    private static readonly Counter<long> Requests = InovaGedTelemetry.Meter.CreateCounter<long>("inovaged.http.server.requests");
    private static readonly Counter<long> Errors = InovaGedTelemetry.Meter.CreateCounter<long>("inovaged.http.server.errors");
    private static readonly Histogram<double> Duration = InovaGedTelemetry.Meter.CreateHistogram<double>("inovaged.http.server.duration", "ms");
    private static readonly UpDownCounter<long> Active = InovaGedTelemetry.Meter.CreateUpDownCounter<long>("inovaged.http.active_requests");
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp(); Active.Add(1);
        try { await next(context); }
        finally
        {
            Active.Add(-1); var route = context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.RouteNameMetadata>()?.RouteName ?? context.GetEndpoint()?.DisplayName ?? "unmatched";
            var statusClass = $"{context.Response.StatusCode / 100}xx"; var tags = new TagList { { "route", route }, { "method", context.Request.Method }, { "status_class", statusClass } };
            Requests.Add(1, tags); if (context.Response.StatusCode >= 500) Errors.Add(1, tags); var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds; Duration.Record(elapsed, tags);
            logger.LogInformation("HTTP request completed {Method} {Route} with {StatusCode} in {DurationMs} ms and {ResponseSize} bytes", context.Request.Method, route, context.Response.StatusCode, elapsed, context.Response.ContentLength);
        }
    }
}
