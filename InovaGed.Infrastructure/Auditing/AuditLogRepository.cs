using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common;
using InovaGed.Domain.Auditing;

namespace InovaGed.Infrastructure.Auditing;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnectionFactory _factory;

    public AuditLogRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(AuditLog log, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO ged.audit_log
            (id, tenant_id, action, entity_name, entity_id, data_json, ip, user_agent, created_at_utc, created_by)
            VALUES
            (@Id, @TenantId, @Action, @EntityName, @EntityId, @DataJson, @Ip, @UserAgent, @CreatedAtUtc, @CreatedBy);";

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, log, cancellationToken: ct));
    }
}
