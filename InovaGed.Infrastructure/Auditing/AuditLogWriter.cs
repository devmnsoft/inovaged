using System.Data;
using Dapper;
using InovaGed.Application.Auditing;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Auditing;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly ILogger<AuditLogWriter> _logger;

    public AuditLogWriter(ILogger<AuditLogWriter> logger)
    {
        _logger = logger;
    }

    public async Task InsertAsync(AuditLogRow row, IDbTransaction tx, CancellationToken ct)
    {
        // ? Schema + colunas reais (bdged.sql):
        // ged.audit_log(id bigint default, tenant_id, event_time, user_id, action, entity_name, entity_id, summary, ip_address, user_agent, entity, data)
        // "details" NÃO existe.
        const string sql = @"
INSERT INTO ged.audit_log (
    tenant_id,
    event_time,
    user_id,
    action,
    entity_name,
    entity_id,
    summary,
    ip_address,
    user_agent
) VALUES (
    @TenantId,
    NOW(),
    @UserId,
    @Action::ged.audit_action_enum,
    @EntityName,
    @EntityId,
    @Summary,
    @IpAddress,
    @UserAgent
);";

        try
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, row, tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao inserir audit_log. Entity={Entity}, Id={Id}",
                row.EntityName, row.EntityId);
            throw;
        }
    }
}
