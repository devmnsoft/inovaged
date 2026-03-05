using System.Diagnostics;
using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Infrastructure.Common.Security;

public sealed class AccessFailureLogger : IAccessFailureLogger
{
    private readonly IDbConnectionFactory _db;
    private readonly ITenantAccessor _tenant;

    public AccessFailureLogger(IDbConnectionFactory db, ITenantAccessor tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task LogAsync(HttpContext ctx, AuthorizationPolicy? policy, int statusCode, string reason)
    {
        // Tenant
        var tenantId = _tenant.GetTenantIdOrThrow(ctx);

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

        using var conn = _db.CreateConnection();

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