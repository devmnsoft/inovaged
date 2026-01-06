using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Workflow;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Workflow;

public sealed class DocumentWorkflowQueries : IDocumentWorkflowQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentWorkflowQueries> _logger;

    public DocumentWorkflowQueries(IDbConnectionFactory db, ILogger<DocumentWorkflowQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DocumentWorkflowStateDto?> GetCurrentAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  dw.id                  AS ""DocumentWorkflowId"",
  dw.workflow_id         AS ""WorkflowId"",
  wd.name                AS ""WorkflowName"",
  dw.current_stage_id    AS ""CurrentStageId"",
  ws.name                AS ""CurrentStageName"",
  dw.is_completed        AS ""IsCompleted"",
  dw.started_at          AS ""StartedAt"",
  dw.started_by          AS ""StartedBy""
FROM ged.document_workflow dw
JOIN ged.workflow_definition wd ON wd.id = dw.workflow_id AND wd.tenant_id = dw.tenant_id
JOIN ged.workflow_stage ws      ON ws.id = dw.current_stage_id AND ws.workflow_id = dw.workflow_id
WHERE dw.tenant_id = @tenantId
  AND dw.document_id = @documentId
ORDER BY dw.started_at DESC
LIMIT 1;";

        try
        {
            using var conn = await _db.OpenAsync(ct);
            return await conn.QueryFirstOrDefaultAsync<DocumentWorkflowStateDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro GetCurrentAsync workflow. Tenant={TenantId}, Doc={DocId}", tenantId, documentId);
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentWorkflowTransitionDto>> ListAvailableTransitionsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
WITH cur AS (
  SELECT dw.workflow_id, dw.current_stage_id
  FROM ged.document_workflow dw
  WHERE dw.tenant_id = @tenantId
    AND dw.document_id = @documentId
    AND COALESCE(dw.is_completed,false) = false
  ORDER BY dw.started_at DESC
  LIMIT 1
)
SELECT
  t.id               AS ""Id"",
  t.name             AS ""Name"",
  t.to_stage_id      AS ""ToStageId"",
  s_to.name          AS ""ToStageName"",
  t.requires_reason  AS ""RequiresReason""
FROM cur
JOIN ged.workflow_transition t ON t.workflow_id = cur.workflow_id AND t.from_stage_id = cur.current_stage_id
JOIN ged.workflow_stage s_to   ON s_to.id = t.to_stage_id
ORDER BY t.name;";

        try
        {
            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DocumentWorkflowTransitionDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ListAvailableTransitionsAsync. Tenant={TenantId}, Doc={DocId}", tenantId, documentId);
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentWorkflowHistoryDto>> ListHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
WITH cur AS (
  SELECT dw.id
  FROM ged.document_workflow dw
  WHERE dw.tenant_id = @tenantId
    AND dw.document_id = @documentId
  ORDER BY dw.started_at DESC
  LIMIT 1
)
SELECT
  h.id                  AS ""Id"",
  s_from.name           AS ""FromStageName"",
  s_to.name             AS ""ToStageName"",
  h.performed_at        AS ""PerformedAt"",
  h.performed_by        AS ""PerformedBy"",
  h.reason              AS ""Reason"",
  h.comments            AS ""Comments""
FROM cur
JOIN ged.document_workflow_history h ON h.document_workflow_id = cur.id
LEFT JOIN ged.workflow_stage s_from  ON s_from.id = h.from_stage_id
JOIN ged.workflow_stage s_to         ON s_to.id = h.to_stage_id
ORDER BY h.performed_at DESC, h.id DESC;";

        try
        {
            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DocumentWorkflowHistoryDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ListHistoryAsync. Tenant={TenantId}, Doc={DocId}", tenantId, documentId);
            throw;
        }
    }


    public async Task<IReadOnlyList<WorkflowLogRow>> ListAsync(Guid tenantId, Guid documentId, int take, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
    l.created_at      AS ""CreatedAt"",
    COALESCE(l.from_status::text,'') AS ""FromStatus"",
    l.to_status::text AS ""ToStatus"",
    l.reason          AS ""Reason"",
    l.created_by      AS ""CreatedBy"",
    COALESCE(u.name, u.email, '') AS ""UserName""
FROM ged.document_workflow_log l
LEFT JOIN core.usuario u ON u.id = l.created_by AND u.tenant_id = l.tenant_id
WHERE l.tenant_id = @tenantId
  AND l.document_id = @documentId
ORDER BY l.created_at DESC
LIMIT @take;";

            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<WorkflowLogRow>(new CommandDefinition(
                sql, new { tenantId, documentId, take }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar workflow log. Tenant={TenantId} Doc={DocId}", tenantId, documentId);
            return Array.Empty<WorkflowLogRow>();
        }
    }
}
