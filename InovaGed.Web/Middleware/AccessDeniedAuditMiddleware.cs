using InovaGed.Application.Audit;
using InovaGed.Application.Identity;

namespace InovaGed.Web.Middleware;

public sealed class AccessDeniedAuditMiddleware
{
    private readonly RequestDelegate _next;

    public AccessDeniedAuditMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, ICurrentUser currentUser, IAuditWriter audit)
    {
        await _next(ctx);

        var userAgent = ctx.Request.Headers.UserAgent.ToString();
        if (ctx.Response.StatusCode == StatusCodes.Status403Forbidden && !IsKnownBot(userAgent))
        {
            await audit.WriteAsync(
                tenantId: currentUser.TenantId,
                userId: currentUser.UserId,
                action: "ACCESS_DENIED", // precisa existir no enum
                entityName: "security",
                entityId: null,
                summary: $"403 em {ctx.Request.Method} {ctx.Request.Path}",
                ipAddress: ctx.Connection.RemoteIpAddress?.ToString(),
                userAgent: userAgent,
                data: new
                {
                    method = ctx.Request.Method,
                    path = ctx.Request.Path.Value
                },
                ct: ctx.RequestAborted);
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
