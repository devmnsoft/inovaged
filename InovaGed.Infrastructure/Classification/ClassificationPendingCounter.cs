using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class ClassificationPendingCounter : IClassificationPendingCounter
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ClassificationPendingCounter> _logger;

    public ClassificationPendingCounter(
        IDbConnectionFactory db,
        ILogger<ClassificationPendingCounter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.status <> 'ARCHIVED'::ged.document_status_enum
  AND (
        d.type_id IS NULL
        OR EXISTS (
            SELECT 1
            FROM ged.document_classification dc
            WHERE dc.tenant_id = d.tenant_id
              AND dc.document_id = d.id
              AND dc.is_pending = true
        )
  );
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao contar pendências de classificação. Tenant={TenantId}", tenantId);

            // ⚠️ NÃO quebra o layout
            return 0;
        }
    }
}
