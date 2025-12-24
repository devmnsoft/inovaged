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
(id, tenant_id, document_id, workflow_id, current_stage_id, started_at, is_completed, created_at, created_by)
VALUES
(@id, @tenantId, @documentId, @workflowId, @stageId, NOW(), FALSE, NOW(), @userId);";

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

        public async Task<Result> ApplyTransitionAsync(Guid tenantId, Guid documentWorkflowId, Guid transitionId, string? reason, string? comments, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (documentWorkflowId == Guid.Empty || transitionId == Guid.Empty)
                    return Result.Fail("VALIDATION", "Workflow do documento e transição são obrigatórios.");

                using var conn = await _db.OpenAsync(ct);
                using var tx = conn.BeginTransaction();

                // carrega workflow atual
                const string sqlDw = @"
SELECT id, tenant_id AS ""TenantId"", workflow_id AS ""WorkflowId"", current_stage_id AS ""CurrentStageId"", COALESCE(is_completed,false) AS ""IsCompleted""
FROM ged.document_workflow
WHERE id = @id AND tenant_id = @tenantId
LIMIT 1;";

                var dw = await conn.QuerySingleOrDefaultAsync<dynamic>(
                    new CommandDefinition(sqlDw, new { id = documentWorkflowId, tenantId }, transaction: tx, cancellationToken: ct));

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

                // valida transição e pega to_stage
                const string sqlTr = @"
SELECT t.to_stage_id AS ""ToStageId"", t.requires_reason AS ""RequiresReason""
FROM ged.workflow_transition t
JOIN ged.workflow_definition w ON w.id = t.workflow_id AND w.tenant_id = @tenantId
WHERE t.id = @transitionId AND t.workflow_id = @workflowId AND t.from_stage_id = @fromStageId
LIMIT 1;";

                var tr = await conn.QuerySingleOrDefaultAsync<dynamic>(
                    new CommandDefinition(sqlTr, new
                    {
                        tenantId,
                        transitionId,
                        workflowId = (Guid)dw.WorkflowId,
                        fromStageId = (Guid)dw.CurrentStageId
                    }, transaction: tx, cancellationToken: ct));

                if (tr is null)
                {
                    tx.Rollback();
                    return Result.Fail("VALIDATION", "Transição inválida para a etapa atual.");
                }

                if ((bool)tr.RequiresReason && string.IsNullOrWhiteSpace(reason))
                {
                    tx.Rollback();
                    return Result.Fail("VALIDATION", "Esta transição exige justificativa.");
                }

                var toStageId = (Guid)tr.ToStageId;

                // atualiza estágio atual
                const string sqlUp = @"
UPDATE ged.document_workflow
SET current_stage_id = @toStageId,
    updated_at = NOW(),
    updated_by = @userId
WHERE id = @id AND tenant_id = @tenantId;";

                await conn.ExecuteAsync(new CommandDefinition(sqlUp, new { id = documentWorkflowId, tenantId, toStageId, userId }, transaction: tx, cancellationToken: ct));

                // conclui se etapa final
                const string sqlIsFinal = @"SELECT is_final FROM ged.workflow_stage WHERE id=@id LIMIT 1;";
                var isFinal = await conn.ExecuteScalarAsync<bool?>(
                    new CommandDefinition(sqlIsFinal, new { id = toStageId }, transaction: tx, cancellationToken: ct)) ?? false;

                if (isFinal)
                {
                    const string sqlDone = @"
UPDATE ged.document_workflow
SET is_completed = TRUE,
    completed_at = NOW(),
    updated_at = NOW(),
    updated_by = @userId
WHERE id = @id AND tenant_id = @tenantId;";
                    await conn.ExecuteAsync(new CommandDefinition(sqlDone, new { id = documentWorkflowId, tenantId, userId }, transaction: tx, cancellationToken: ct));
                }

                // histórico
                const string sqlHist = @"
INSERT INTO ged.document_workflow_history
(document_workflow_id, from_stage_id, to_stage_id, performed_by, performed_at, reason, comments)
VALUES
(@documentWorkflowId, @fromStageId, @toStageId, @userId, NOW(), @reason, @comments);";

                await conn.ExecuteAsync(new CommandDefinition(sqlHist, new
                {
                    documentWorkflowId,
                    fromStageId = (Guid)dw.CurrentStageId,
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
