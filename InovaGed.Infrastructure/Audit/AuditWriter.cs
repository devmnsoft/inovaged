using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Audit
{
    public sealed class AuditWriter : IAuditWriter
    {
        private readonly IDbConnectionFactory _db;
        private readonly ILogger<AuditWriter> _logger;

        public AuditWriter(IDbConnectionFactory db, ILogger<AuditWriter> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task WriteAsync(Guid tenantId, Guid? userId, string? userDisplay,
            string action, string? entityType, Guid? entityId,
            bool success, object? details, string? ip, string? userAgent,
            CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);

                var sql = """
            INSERT INTO ged.audit_log(id, tenant_id, at, user_id, user_display, action, entity_type, entity_id, success, details, ip, user_agent)
            VALUES (@Id, @TenantId, now(), @UserId, @UserDisplay, @Action, @EntityType, @EntityId, @Success, CAST(@Details AS jsonb), @Ip, @UserAgent);
            """;

                var p = new
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    UserDisplay = userDisplay,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Success = success,
                    Details = details == null ? null : System.Text.Json.JsonSerializer.Serialize(details),
                    Ip = ip,
                    UserAgent = userAgent
                };

                await con.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuditWriter failed. Tenant={TenantId} Action={Action}", tenantId, action);
            }
        }
    }
}
