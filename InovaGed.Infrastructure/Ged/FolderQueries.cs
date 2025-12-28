using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged;

public sealed class FolderQueries : IFolderQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<FolderQueries> _logger;

    public FolderQueries(IDbConnectionFactory db, ILogger<FolderQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FolderNodeDto>> TreeAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Carregando árvore de pastas. Tenant={TenantId}", tenantId);

            const string sql = @"
WITH RECURSIVE t AS (
    SELECT 
        f.id,
        f.parent_id AS ""ParentId"",
        f.name      AS ""Name"",
        0           AS ""Level""
    FROM ged.folder f
    WHERE f.tenant_id = @tenantId
      AND f.parent_id IS NULL

    UNION ALL

    SELECT
        c.id,
        c.parent_id AS ""ParentId"",
        c.name      AS ""Name"",
        t.""Level"" + 1 AS ""Level""
    FROM ged.folder c
    JOIN t ON t.id = c.parent_id
    WHERE c.tenant_id = @tenantId
)
SELECT * FROM t
ORDER BY ""Level"", ""Name"";";

            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<FolderNodeDto>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Carregamento da árvore de pastas foi cancelado. Tenant={TenantId}", tenantId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao carregar árvore de pastas. Tenant={TenantId}", tenantId);
            throw;
        }
    }

}
