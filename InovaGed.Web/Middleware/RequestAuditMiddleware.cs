using System.Diagnostics;
using InovaGed.Application.Audit;
using InovaGed.Application.Identity;

namespace InovaGed.Web.Middleware;

public sealed class RequestAuditMiddleware
{
    private static readonly string[] StaticPrefixes = ["/css", "/js", "/lib", "/images", "/favicon", "/health", "/robots.txt"];
    private readonly RequestDelegate _next;
    public RequestAuditMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, ICurrentUser currentUser, IAppAuditLogService audit)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        var correlationId = ctx.TraceIdentifier;
        ctx.Response.Headers["X-Correlation-Id"] = correlationId;
        var userAgent = ctx.Request.Headers.UserAgent.ToString();
        var isKnownBot = IsKnownBot(userAgent);
        if (StaticPrefixes.Any(path.StartsWith) || path.EndsWith(".css") || path.EndsWith(".js") || path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".svg")) { await _next(ctx); return; }
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(ctx);
            sw.Stop();
            if (!isKnownBot && (ctx.Response.StatusCode >= 400 || path.StartsWith("/Account") || path.StartsWith("/Ged") || path.StartsWith("/Loans") || path.StartsWith("/Users") || path.StartsWith("/GedDashboard") || path.StartsWith("/GedReports")))
            {
                var isAccessDenied = ctx.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
                var action = isAccessDenied ? "ACCESS_DENIED" : "HTTP";
                var eventType = isAccessDenied
                    ? "ACCESS_DENIED"
                    : ctx.Response.StatusCode >= 500 ? "ERROR" : "INFO";

                await audit.LogAsync(new AppAuditLogEntry
                {
                    TenantId = currentUser.TenantId,
                    UserId = currentUser.UserId,
                    UserName = ctx.User.Identity?.Name,
                    EventType = eventType,
                    Action = action,
                    Source = "RequestAuditMiddleware",
                    EntityName = "HTTP_REQUEST",
                    EntityId = null,
                    Summary = $"{ctx.Request.Method} {path} => {ctx.Response.StatusCode}",
                    Path = path,
                    HttpMethod = ctx.Request.Method,
                    HttpStatus = ctx.Response.StatusCode,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    IpAddress = ctx.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = userAgent,
                    CorrelationId = correlationId,
                    Data = new
                    {
                        queryString = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : null,
                        routeValues = ctx.Request.RouteValues.ToDictionary(k => k.Key, v => v.Value?.ToString()),
                        correlationId
                    }
                }, ctx.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!isKnownBot)
            {
                await audit.LogAsync(new AppAuditLogEntry { TenantId = currentUser.TenantId, UserId = currentUser.UserId, EventType = "ERROR", Action = "HTTP", Source = "RequestAuditMiddleware", EntityName = "HTTP_REQUEST", EntityId = null, Summary = "Unhandled HTTP exception", Path = path, HttpMethod = ctx.Request.Method, HttpStatus = 500, ExceptionType = ex.GetType().Name, ExceptionMessage = ex.Message, StackTrace = ex.StackTrace, CorrelationId = correlationId }, CancellationToken.None);
            }
            throw;
        }
    }

    private static bool IsKnownBot(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return false;
        return userAgent.Contains("GPTBot", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("OAI-SearchBot", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Bingbot", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Google-Read-Aloud", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Googlebot", StringComparison.OrdinalIgnoreCase);
    }
}
