using InovaGed.Application.Audit;
using InovaGed.Application.Identity;

namespace InovaGed.Web.Middleware;

public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, ICurrentUser currentUser, IAuditWriter audit)
    {
        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId; // Guid?
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var ua = ctx.Request.Headers.UserAgent.ToString();

        try
        {
            await _next(ctx);

            // Auditoria "HTTP" (evidência PoC e trilha completa)
            await audit.WriteAsync(
                tenantId: tenantId,
                userId: userId,
                action: "HTTP",                 // precisa existir no enum ged.audit_action_enum
                entityName: "http_request",
                entityId: null,
                summary: $"{ctx.Request.Method} {ctx.Request.Path}",
                ipAddress: ip,
                userAgent: ua,
                data: new
                {
                    method = ctx.Request.Method,
                    path = ctx.Request.Path.Value,
                    status = ctx.Response.StatusCode
                },
                ct: ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            // erro também registra (mas nunca derruba)
            await audit.WriteAsync(
                tenantId: tenantId,
                userId: userId,
                action: "ERROR",                // precisa existir no enum
                entityName: "http_request",
                entityId: null,
                summary: $"EXCEPTION {ctx.Request.Method} {ctx.Request.Path}: {ex.Message}",
                ipAddress: ip,
                userAgent: ua,
                data: new { ex.Message, ex.StackTrace },
                ct: CancellationToken.None);

            throw;
        }
    }
}