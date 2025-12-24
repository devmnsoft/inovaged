using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Workflow;
using InovaGed.Domain.Workflow;
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

    public async Task<DocumentWorkflowCurrentDto?> GetCurrentAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
    dw.id                 AS ""Id"",
    dw.document_id        AS ""DocumentId"",
    dw.workflow_id        AS ""WorkflowId"",
    dw.current_stage_id   AS ""CurrentStageId"",
    ws.name               AS ""CurrentStageName"",
    ws.is_final           AS ""IsFinal""
FROM ged.document_workflow dw
JOIN ged.workflow_stage ws
  ON ws.id = dw.current_stage_id
JOIN ged.workflow_definition wd
  ON wd.id = dw.workflow_id
WHERE dw.tenant_id = @tenantId
  AND dw.document_id = @documentId
  AND wd.is_active = TRUE
LIMIT 1;";

            using var conn = await _db.OpenAsync(ct);

            return await conn.QueryFirstOrDefaultAsync<DocumentWorkflowCurrentDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar workflow do documento.");
            return null;
        }
    }

    // ✅ Alinhado ao seu schema real (bdged.sql):
    // ged.document_workflow_history NÃO tem tenant_id, document_id e action.
    // Filtramos por documento via JOIN em ged.document_workflow.
    public async Task<IReadOnlyList<DocumentWorkflowHistoryRowDto>> ListHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
    h.id                      AS ""Id"",
    h.document_workflow_id    AS ""DocumentWorkflowId"",
    h.from_stage_id           AS ""FromStageId"",
    h.to_stage_id             AS ""ToStageId"",
    h.performed_by            AS ""PerformedBy"",
    h.performed_at            AS ""PerformedAt"",
    h.reason                  AS ""Reason"",
    h.comments                AS ""Comments""
FROM ged.document_workflow_history h
JOIN ged.document_workflow dw
  ON dw.id = h.document_workflow_id
WHERE dw.tenant_id = @tenantId
  AND dw.document_id = @documentId
ORDER BY h.performed_at DESC, h.id DESC;";

            using var conn = await _db.OpenAsync(ct);

            var rows = await conn.QueryAsync<DocumentWorkflowHistoryRowDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar histórico do workflow do documento.");
            return Array.Empty<DocumentWorkflowHistoryRowDto>();
        }
    }
}
