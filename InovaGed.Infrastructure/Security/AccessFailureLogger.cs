using System.Diagnostics;
using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Common.Security;

public sealed class AccessFailureLogger : IAccessFailureLogger
{
    private readonly IDbConnectionFactory _db;
    private readonly ITenantAccessor _tenant;
    private readonly ILogger<AccessFailureLogger> _logger;

    public AccessFailureLogger(IDbConnectionFactory db, ITenantAccessor tenant, ILogger<AccessFailureLogger> logger)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task LogAsync(HttpContext ctx, AuthorizationPolicy? policy, int statusCode, string reason)
    {
        // Tenant
        var tenantId = ResolveTenantId(ctx);

        // User (tenta pegar Guid do NameIdentifier; se não tiver, fica null)
        Guid? userId = null;
        var userIdClaim = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userIdClaim) && Guid.TryParse(userIdClaim, out var uid))
            userId = uid;

        var userName =
            ctx.User?.Identity?.Name ??
            ctx.User?.FindFirst("name")?.Value ??
            ctx.User?.FindFirst(ClaimTypes.Name)?.Value;

        var userEmail =
            ctx.User?.FindFirst(ClaimTypes.Email)?.Value ??
            ctx.User?.FindFirst("email")?.Value;

        // ✅ Corrige teu erro CS1061:
        // IHeaderDictionary não tem .UserAgent. Use o header "User-Agent".
        var userAgent = ctx.Request.Headers.TryGetValue("User-Agent", out var ua)
            ? ua.ToString()
            : null;

        var ip = ctx.Connection.RemoteIpAddress?.ToString();

        var requiredRoles = ExtractRoles(policy);

        var correlationId = ctx.TraceIdentifier;
        var traceId = Activity.Current?.TraceId.ToString();

        const string sql = @"
insert into ged.security_access_failure_log
(id, tenant_id, happened_at, user_id, user_name, user_email,
 http_method, path, query_string, status_code,
 required_roles, ip, user_agent, correlation_id, trace_id, reason)
values
(@Id, @TenantId, now(), @UserId, @UserName, @UserEmail,
 @Method, @Path, @Query, @StatusCode,
 @RequiredRoles, @Ip, @UserAgent, @CorrelationId, @TraceId, @Reason);";

        var roles = ctx.User?.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var controller = ctx.Request.RouteValues.TryGetValue("controller", out var controllerValue) ? controllerValue?.ToString() : null;
        var action = ctx.Request.RouteValues.TryGetValue("action", out var actionValue) ? actionValue?.ToString() : null;

        if (statusCode == StatusCodes.Status403Forbidden && roles.Any(r => IsRole(r, "ADMIN") || IsRole(r, "ADMINISTRADOR")))
        {
            _logger.LogWarning("Full admin recebeu 403. UserId={UserId} UserName={UserName} Roles={Roles} Path={Path} Method={Method} Controller={Controller} Action={Action} CorrelationId={CorrelationId}",
                userId, userName, string.Join(",", roles), ctx.Request.Path.Value, ctx.Request.Method, controller, action, correlationId);
        }

        using var conn = _db.CreateConnection();

        if (tenantId != Guid.Empty && await SecurityFailureLogExistsAsync(conn))
        {
            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,

                UserId = userId,
                UserName = userName,
                UserEmail = userEmail,

                Method = ctx.Request.Method,
                Path = ctx.Request.Path.Value ?? "",
                Query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : null,
                StatusCode = statusCode,

                RequiredRoles = requiredRoles,

                Ip = ip,
                UserAgent = userAgent,
                CorrelationId = correlationId,
                TraceId = traceId,
                Reason = reason
            });
        }

        if (statusCode == StatusCodes.Status403Forbidden && tenantId != Guid.Empty && await AppAuditLogExistsAsync(conn))
        {
            const string auditSql = @"
insert into ged.app_audit_log
(id, tenant_id, user_id, user_name, action, event_type, source, entity_name, method, path, status_code, message, details, correlation_id, ip_address, user_agent, created_at, reg_status)
values
(gen_random_uuid(), @TenantId, @UserId, @UserName, 'ACCESS_DENIED', 'WARNING', 'Authorization', 'security', @Method, @Path, 403, @Message, @Details::jsonb, @CorrelationId, @Ip, @UserAgent, now(), 'A');";
            var details = System.Text.Json.JsonSerializer.Serialize(new
            {
                userId,
                userName,
                roles,
                path = ctx.Request.Path.Value,
                method = ctx.Request.Method,
                policy = requiredRoles,
                controller,
                action,
                ip,
                userAgent,
                correlationId,
                created_at = DateTimeOffset.UtcNow
            });

            await conn.ExecuteAsync(auditSql, new
            {
                TenantId = tenantId,
                UserId = userId,
                UserName = userName,
                Method = ctx.Request.Method,
                Path = ctx.Request.Path.Value ?? string.Empty,
                Message = $"ACCESS_DENIED em {ctx.Request.Method} {ctx.Request.Path}",
                Details = details,
                CorrelationId = correlationId,
                Ip = ip,
                UserAgent = userAgent
            });
        }
    }

    private Guid ResolveTenantId(HttpContext ctx)
    {
        try
        {
            return _tenant.GetTenantIdOrThrow(ctx);
        }
        catch
        {
            var raw = ctx.User?.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(raw, out var tenantId) ? tenantId : Guid.Empty;
        }
    }

    private static async Task<bool> SecurityFailureLogExistsAsync(System.Data.IDbConnection conn)
        => await conn.ExecuteScalarAsync<bool>("select to_regclass('ged.security_access_failure_log') is not null;");

    private static async Task<bool> AppAuditLogExistsAsync(System.Data.IDbConnection conn)
        => await conn.ExecuteScalarAsync<bool>("select to_regclass('ged.app_audit_log') is not null;");

    private static bool IsRole(string? value, string role)
        => string.Equals(NormalizeRole(value), NormalizeRole(role), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRole(string? value)
        => (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();

    private static string? ExtractRoles(AuthorizationPolicy? policy)
    {
        if (policy == null) return null;

        // Pega roles caso você use [Authorize(Roles="...")]
        var roles = policy.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(r => r.AllowedRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return roles.Length == 0 ? null : string.Join(", ", roles);
    }
}