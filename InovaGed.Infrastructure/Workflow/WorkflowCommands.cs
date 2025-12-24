    using Dapper;
    using InovaGed.Application.Common.Database;
    using InovaGed.Application.Workflow;
    using InovaGed.Domain.Primitives;
using InovaGed.Domain.Workflow;
using Microsoft.Extensions.Logging;

    namespace InovaGed.Infrastructure.Workflow;

    public sealed class WorkflowCommands : IWorkflowCommands
    {
        private readonly IDbConnectionFactory _db;
        private readonly ILogger<WorkflowCommands> _logger;

        public WorkflowCommands(IDbConnectionFactory db, ILogger<WorkflowCommands> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Result<Guid>> CreateDefinitionAsync(Guid tenantId, CreateWorkflowDefinitionCommand cmd, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cmd.Code) || string.IsNullOrWhiteSpace(cmd.Name))
                    return Result<Guid>.Fail("VALIDATION", "Código e nome são obrigatórios.");

                var id = Guid.NewGuid();

                const string sql = @"
INSERT INTO ged.workflow_definition
(id, tenant_id, name, code, description, is_active, created_at, created_by)
VALUES
(@id, @tenantId, @name, @code, @description, TRUE, NOW(), @userId);";

                using var conn = await _db.OpenAsync(ct);
                await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    id,
                    tenantId,
                    name = cmd.Name.Trim(),
                    code = cmd.Code.Trim(),
                    description = string.IsNullOrWhiteSpace(cmd.Description) ? null : cmd.Description.Trim(),
                    userId
                }, cancellationToken: ct));

                return Result<Guid>.Ok(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar workflow.");
                return Result<Guid>.Fail("ERR", "Erro ao criar workflow.");
            }
        }

        public async Task<Result> UpdateDefinitionAsync(Guid tenantId, UpdateWorkflowDefinitionCommand cmd, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (cmd.Id == Guid.Empty)
                    return Result.Fail("VALIDATION", "Registro inválido.");

                const string sql = @"
UPDATE ged.workflow_definition
SET
  name = @name,
  code = @code,
  description = @description,
  is_active = @isActive,
  updated_at = NOW(),
  updated_by = @userId
WHERE tenant_id = @tenantId AND id = @id;";

                using var conn = await _db.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    tenantId,
                    id = cmd.Id,
                    name = cmd.Name.Trim(),
                    code = cmd.Code.Trim(),
                    description = string.IsNullOrWhiteSpace(cmd.Description) ? null : cmd.Description.Trim(),
                    isActive = cmd.IsActive,
                    userId
                }, cancellationToken: ct));

                if (rows == 0) return Result.Fail("NOT_FOUND", "Workflow não encontrado.");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar workflow.");
                return Result.Fail("ERR", "Erro ao atualizar workflow.");
            }
        }

        public async Task<Result> DeactivateDefinitionAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct)
        {
            try
            {
                const string sql = @"
UPDATE ged.workflow_definition
SET is_active = FALSE, updated_at = NOW(), updated_by = @userId
WHERE tenant_id = @tenantId AND id = @id;";

                using var conn = await _db.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, id, userId }, cancellationToken: ct));
                if (rows == 0) return Result.Fail("NOT_FOUND", "Workflow não encontrado.");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao desativar workflow.");
                return Result.Fail("ERR", "Erro ao desativar workflow.");
            }
        }

        public async Task<Result<Guid>> CreateStageAsync(Guid tenantId, CreateWorkflowStageCommand cmd, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (cmd.WorkflowId == Guid.Empty) return Result<Guid>.Fail("VALIDATION", "Workflow inválido.");
                if (string.IsNullOrWhiteSpace(cmd.Code) || string.IsNullOrWhiteSpace(cmd.Name))
                    return Result<Guid>.Fail("VALIDATION", "Código e nome são obrigatórios.");

                // valida tenant do workflow
                const string sqlCheck = @"SELECT 1 FROM ged.workflow_definition WHERE tenant_id=@tenantId AND id=@id;";
                const string sql = @"
INSERT INTO ged.workflow_stage
(id, workflow_id, name, code, sort_order, is_start, is_final, required_role, created_at)
VALUES
(@id, @workflowId, @name, @code, @sortOrder, @isStart, @isFinal, @requiredRole, NOW());";

                using var conn = await _db.OpenAsync(ct);
                var ok = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sqlCheck, new { tenantId, id = cmd.WorkflowId }, cancellationToken: ct));
                if (ok is null) return Result<Guid>.Fail("NOT_FOUND", "Workflow não encontrado.");

                var id = Guid.NewGuid();
                await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    id,
                    workflowId = cmd.WorkflowId,
                    name = cmd.Name.Trim(),
                    code = cmd.Code.Trim(),
                    sortOrder = cmd.SortOrder,
                    isStart = cmd.IsStart,
                    isFinal = cmd.IsFinal,
                    requiredRole = string.IsNullOrWhiteSpace(cmd.RequiredRole) ? null : cmd.RequiredRole.Trim()
                }, cancellationToken: ct));

                return Result<Guid>.Ok(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar etapa.");
                return Result<Guid>.Fail("ERR", "Erro ao criar etapa.");
            }
        }

        public async Task<Result> UpdateStageAsync(Guid tenantId, UpdateWorkflowStageCommand cmd, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (cmd.Id == Guid.Empty) return Result.Fail("VALIDATION", "Etapa inválida.");

                const string sql = @"
UPDATE ged.workflow_stage s
SET
  name = @name,
  code = @code,
  sort_order = @sortOrder,
  is_start = @isStart,
  is_final = @isFinal,
  required_role = @requiredRole
FROM ged.workflow_definition w
WHERE w.tenant_id = @tenantId
  AND w.id = s.workflow_id
  AND s.id = @id;";

                using var conn = await _db.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    tenantId,
                    id = cmd.Id,
                    name = cmd.Name.Trim(),
                    code = cmd.Code.Trim(),
                    sortOrder = cmd.SortOrder,
                    isStart = cmd.IsStart,
                    isFinal = cmd.IsFinal,
                    requiredRole = string.IsNullOrWhiteSpace(cmd.RequiredRole) ? null : cmd.RequiredRole.Trim()
                }, cancellationToken: ct));

                if (rows == 0) return Result.Fail("NOT_FOUND", "Etapa não encontrada.");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar etapa.");
                return Result.Fail("ERR", "Erro ao atualizar etapa.");
            }
        }

        public async Task<Result> DeleteStageAsync(Guid tenantId, Guid id, CancellationToken ct)
        {
            try
            {
                const string sql = @"
DELETE FROM ged.workflow_stage s
USING ged.workflow_definition w
WHERE w.tenant_id = @tenantId
  AND w.id = s.workflow_id
  AND s.id = @id;";

                using var conn = await _db.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, id }, cancellationToken: ct));
                if (rows == 0) return Result.Fail("NOT_FOUND", "Etapa não encontrada.");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao excluir etapa.");
                return Result.Fail("ERR", "Erro ao excluir etapa.");
            }
        }

        public async Task<Result<Guid>> CreateTransitionAsync(Guid tenantId, CreateWorkflowTransitionCommand cmd, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (cmd.WorkflowId == Guid.Empty) return Result<Guid>.Fail("VALIDATION", "Workflow inválido.");
                if (cmd.FromStageId == Guid.Empty || cmd.ToStageId == Guid.Empty)
                    return Result<Guid>.Fail("VALIDATION", "Etapas inválidas.");
                if (string.IsNullOrWhiteSpace(cmd.Name))
                    return Result<Guid>.Fail("VALIDATION", "Nome da transição é obrigatório.");

                const string sqlCheck = @"SELECT 1 FROM ged.workflow_definition WHERE tenant_id=@tenantId AND id=@id;";
                const string sql = @"
INSERT INTO ged.workflow_transition
(id, tenant_id, workflow_id, from_stage_id, to_stage_id, name, requires_reason, created_at)
VALUES
(@id, @tenantId, @workflowId, @fromId, @toId, @name, @requiresReason, NOW());";

                using var conn = await _db.OpenAsync(ct);
                var ok = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sqlCheck, new { tenantId, id = cmd.WorkflowId }, cancellationToken: ct));
                if (ok is null) return Result<Guid>.Fail("NOT_FOUND", "Workflow não encontrado.");

                var id = Guid.NewGuid();
                await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    id,
                    tenantId,
                    workflowId = cmd.WorkflowId,
                    fromId = cmd.FromStageId,
                    toId = cmd.ToStageId,
                    name = cmd.Name.Trim(),
                    requiresReason = cmd.RequiresReason
                }, cancellationToken: ct));

                return Result<Guid>.Ok(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar transição.");
                return Result<Guid>.Fail("ERR", "Erro ao criar transição.");
            }
        }

        public async Task<Result> UpdateTransitionAsync(Guid tenantId, UpdateWorkflowTransitionCommand cmd, Guid? userId, CancellationToken ct)
        {
            try
            {
                if (cmd.Id == Guid.Empty) return Result.Fail("VALIDATION", "Transição inválida.");

                const string sql = @"
UPDATE ged.workflow_transition
SET
  from_stage_id = @fromId,
  to_stage_id = @toId,
  name = @name,
  requires_reason = @requiresReason
WHERE tenant_id = @tenantId AND id = @id;";

                using var conn = await _db.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    tenantId,
                    id = cmd.Id,
                    fromId = cmd.FromStageId,
                    toId = cmd.ToStageId,
                    name = cmd.Name.Trim(),
                    requiresReason = cmd.RequiresReason
                }, cancellationToken: ct));

                if (rows == 0) return Result.Fail("NOT_FOUND", "Transição não encontrada.");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar transição.");
                return Result.Fail("ERR", "Erro ao atualizar transição.");
            }
        }

        public async Task<Result> DeleteTransitionAsync(Guid tenantId, Guid id, CancellationToken ct)
        {
            try
            {
                const string sql = @"DELETE FROM ged.workflow_transition WHERE tenant_id=@tenantId AND id=@id;";
                using var conn = await _db.OpenAsync(ct);
                var rows = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, id }, cancellationToken: ct));
                if (rows == 0) return Result.Fail("NOT_FOUND", "Transição não encontrada.");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao excluir transição.");
                return Result.Fail("ERR", "Erro ao excluir transição.");
            }
        }
    }
