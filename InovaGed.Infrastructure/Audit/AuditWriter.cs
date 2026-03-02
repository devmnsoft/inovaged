using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Audit;

public sealed class AuditWriter : IAuditWriter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(IDbConnectionFactory db, ILogger<AuditWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result> WriteAsync(
        Guid tenantId,
        Guid? userId,
        string action,
        string entityName,
        Guid? entityId,
        string? summary,
        string? ipAddress,
        string? userAgent,
        object? data,
        CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (string.IsNullOrWhiteSpace(action)) return Result.Fail("ACTION", "Action inválida.");
            if (string.IsNullOrWhiteSpace(entityName)) return Result.Fail("ENTITY", "EntityName inválido.");

            using var conn = await _db.OpenAsync(ct);

            var json = data is null ? null : JsonSerializer.Serialize(data);

            const string sql = @"
insert into ged.audit_log
(tenant_id, user_id, action, entity_name, entity_id, summary, ip_address, user_agent, data)
values
(@tenant_id, @user_id, @action::ged.audit_action_enum, @entity_name, @entity_id, @summary, @ip_address, @user_agent, @data::jsonb);
";

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenant_id = tenantId,
                user_id = userId,
                action,
                entity_name = entityName,
                entity_id = entityId,
                summary,
                ip_address = ipAddress,
                user_agent = userAgent,
                data = json
            }, cancellationToken: ct));

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AuditWriter.WriteAsync failed. Tenant={Tenant} Action={Action} Entity={Entity} Id={Id}",
                tenantId, action, entityName, entityId);

            // auditoria não pode derrubar o fluxo principal
            return Result.Fail("AUDIT", "Falha ao registrar auditoria.");
        }
    }
}