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

        if (ctx.Response.StatusCode == StatusCodes.Status403Forbidden)
        {
            await audit.WriteAsync(
                tenantId: currentUser.TenantId,
                userId: currentUser.UserId,
                action: "ACCESS_DENIED", // precisa existir no enum
                entityName: "security",
                entityId: null,
                summary: $"403 em {ctx.Request.Method} {ctx.Request.Path}",
                ipAddress: ctx.Connection.RemoteIpAddress?.ToString(),
                userAgent: ctx.Request.Headers.UserAgent.ToString(),
                data: new
                {
                    method = ctx.Request.Method,
                    path = ctx.Request.Path.Value
                },
                ct: ctx.RequestAborted);
        }
    }
}