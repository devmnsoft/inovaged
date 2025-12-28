using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Workflow;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Workflow;

public sealed class DocumentWorkflowCommands : IDocumentWorkflowCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DocumentWorkflowCommands> _logger;

    public DocumentWorkflowCommands(IDbConnectionFactory db, ILogger<DocumentWorkflowCommands> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<Guid>> StartAsync(Guid tenantId, Guid documentId, Guid workflowId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (documentId == Guid.Empty || workflowId == Guid.Empty)
                return Result<Guid>.Fail("VALIDATION", "Documento e workflow são obrigatórios.");

            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // pega etapa inicial
            const string sqlStartStage = @"
SELECT s.id
FROM ged.workflow_stage s
JOIN ged.workflow_definition w ON w.id = s.workflow_id AND w.tenant_id = @tenantId
WHERE s.workflow_id = @workflowId AND s.is_start = TRUE
ORDER BY s.sort_order
LIMIT 1;";

            var startStageId = await conn.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(sqlStartStage, new { tenantId, workflowId }, transaction: tx, cancellationToken: ct));

            if (startStageId is null)
            {
                tx.Rollback();
                return Result<Guid>.Fail("VALIDATION", "Workflow não possui etapa inicial (is_start=true).");
            }

            // cria document_workflow
            var id = Guid.NewGuid();

            const string sqlInsert = @"
INSERT INTO ged.document_workflow
(id, tenant_id, document_id, workflow_id, current_stage_id, started_at, started_by, is_completed, created_at, created_by)
VALUES
(@id, @tenantId, @documentId, @workflowId, @stageId, NOW(), @userId, FALSE, NOW(), @userId);";

            await conn.ExecuteAsync(new CommandDefinition(sqlInsert, new
            {
                id,
                tenantId,
                documentId,
                workflowId,
                stageId = startStageId.Value,
                userId
            }, transaction: tx, cancellationToken: ct));

            // registra histórico inicial
            const string sqlHist = @"
INSERT INTO ged.document_workflow_history
(document_workflow_id, from_stage_id, to_stage_id, performed_by, performed_at, reason, comments)
VALUES
(@documentWorkflowId, NULL, @toStageId, @userId, NOW(), 'INICIO', NULL);";

            await conn.ExecuteAsync(new CommandDefinition(sqlHist, new
            {
                documentWorkflowId = id,
                toStageId = startStageId.Value,
                userId
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();
            return Result<Guid>.Ok(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao iniciar workflow do documento.");
            return Result<Guid>.Fail("ERR", "Erro ao iniciar workflow do documento.");
        }
    }

    public async Task<Result> ApplyTransitionAsync(
        Guid tenantId,
        Guid documentWorkflowId,
        Guid transitionId,
        string? reason,
        string? comments,
        Guid? userId,
        CancellationToken ct)
    {
        try
        {
            if (documentWorkflowId == Guid.Empty || transitionId == Guid.Empty)
                return Result.Fail("VALIDATION", "Workflow do documento e transição são obrigatórios.");

            using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // carrega workflow atual
            const string sqlDw = @"
SELECT
  dw.id            AS ""Id"",
  dw.workflow_id   AS ""WorkflowId"",
  dw.current_stage_id AS ""CurrentStageId"",
  COALESCE(dw.is_completed,false) AS ""IsCompleted""
FROM ged.document_workflow dw
WHERE dw.id = @id AND dw.tenant_id = @tenantId
LIMIT 1;";

            var dw = await conn.QueryFirstOrDefaultAsync(
                new CommandDefinition(sqlDw, new { tenantId, id = documentWorkflowId }, transaction: tx, cancellationToken: ct));

            if (dw is null)
            {
                tx.Rollback();
                return Result.Fail("NOT_FOUND", "Workflow do documento não encontrado.");
            }

            if ((bool)dw.IsCompleted)
            {
                tx.Rollback();
                return Result.Fail("VALIDATION", "Workflow já concluído.");
            }

            var workflowId = (Guid)dw.WorkflowId;
            var fromStageId = (Guid)dw.CurrentStageId;

            // valida transição e pega destino
            const string sqlTr = @"
SELECT
  t.to_stage_id      AS ""ToStageId"",
  t.requires_reason  AS ""RequiresReason""
FROM ged.workflow_transition t
WHERE t.id = @transitionId
  AND t.workflow_id = @workflowId
  AND t.from_stage_id = @fromStageId
LIMIT 1;";

            var tr = await conn.QueryFirstOrDefaultAsync(
                new CommandDefinition(sqlTr, new { transitionId, workflowId, fromStageId }, transaction: tx, cancellationToken: ct));

            if (tr is null)
            {
                tx.Rollback();
                return Result.Fail("VALIDATION", "Transição inválida para a etapa atual.");
            }

            var toStageId = (Guid)tr.ToStageId;
            var requiresReason = (bool)tr.RequiresReason;

            if (requiresReason && string.IsNullOrWhiteSpace(reason))
            {
                tx.Rollback();
                return Result.Fail("VALIDATION", "Motivo é obrigatório para esta transição.");
            }

            // verifica se etapa destino é final
            const string sqlIsFinal = @"
SELECT COALESCE(s.is_final,false)
FROM ged.workflow_stage s
WHERE s.id = @stageId AND s.workflow_id = @workflowId
LIMIT 1;";

            var isFinal = await conn.ExecuteScalarAsync<bool>(
                new CommandDefinition(sqlIsFinal, new { stageId = toStageId, workflowId }, transaction: tx, cancellationToken: ct));

            // atualiza current_stage_id / finaliza se for final
            const string sqlUpd = @"
UPDATE ged.document_workflow
SET
  current_stage_id = @toStageId,
  is_completed = @isCompleted,
  updated_at = NOW(),
  updated_by = @userId
WHERE tenant_id = @tenantId AND id = @documentWorkflowId;";

            await conn.ExecuteAsync(new CommandDefinition(sqlUpd, new
            {
                tenantId,
                documentWorkflowId,
                toStageId,
                isCompleted = isFinal,
                userId
            }, transaction: tx, cancellationToken: ct));

            // grava histórico
            const string sqlHist = @"
INSERT INTO ged.document_workflow_history
(document_workflow_id, from_stage_id, to_stage_id, performed_by, performed_at, reason, comments)
VALUES
(@documentWorkflowId, @fromStageId, @toStageId, @userId, NOW(), @reason, @comments);";

            await conn.ExecuteAsync(new CommandDefinition(sqlHist, new
            {
                documentWorkflowId,
                fromStageId,
                toStageId,
                userId,
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim()
            }, transaction: tx, cancellationToken: ct));

            tx.Commit();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao aplicar transição.");
            return Result.Fail("ERR", "Erro ao aplicar transição.");
        }
    }
}
