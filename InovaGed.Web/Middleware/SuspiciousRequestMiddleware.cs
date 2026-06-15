using System.Text.RegularExpressions;

namespace InovaGed.Web.Middleware;

public sealed class SuspiciousRequestMiddleware
{
    private static readonly Regex SensitivePathRegex = new(
        @"(^|/)(\.env($|\.)|env($|\.)|\.git($|/)|\.svn($|/)|appsettings(\..*)?\.json$|web\.config$|package\.json$|composer\.json$|vendor($|/)|node_modules($|/)|phpunit($|/)|cgi-bin($|/)|manager($|/)|actuator($|/)|containers/json$|v2/_catalog$|shell\.aspx?$|webshell($|/)|Telerik\.Web\.UI\.DialogHandler\.aspx$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly RequestDelegate _next;
    private readonly ILogger<SuspiciousRequestMiddleware> _logger;

    public SuspiciousRequestMiddleware(RequestDelegate next, ILogger<SuspiciousRequestMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsSuspicious(path))
        {
            var query = context.Request.QueryString.Value ?? string.Empty;
            if (query.Length > 256) query = query[..256] + "...";
            var correlationId = context.TraceIdentifier;
            _logger.LogWarning(
                "SECURITY_SUSPICIOUS_REQUEST Ip={Ip} UserAgent={UserAgent} Method={Method} Path={Path} QueryString={QueryString} Timestamp={Timestamp:o} CorrelationId={CorrelationId}",
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                context.Request.Method,
                path,
                query,
                DateTimeOffset.UtcNow,
                correlationId);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Not found", context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private static bool IsSuspicious(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/').Trim();
        return SensitivePathRegex.IsMatch(normalized);
    }
}
