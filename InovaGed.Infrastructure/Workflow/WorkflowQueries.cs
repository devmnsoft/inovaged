using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Workflow;
using InovaGed.Domain.Workflow;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Workflow;

public sealed class WorkflowQueries : IWorkflowQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<WorkflowQueries> _logger;

    public WorkflowQueries(IDbConnectionFactory db, ILogger<WorkflowQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkflowDefinitionRowDto>> ListDefinitionsAsync(Guid tenantId, string? q, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
  id        AS ""Id"",
  code      AS ""Code"",
  name      AS ""Name"",
  is_active AS ""IsActive""
FROM ged.workflow_definition
WHERE tenant_id = @tenantId
  AND (@q IS NULL OR code ILIKE ('%'||@q||'%') OR name ILIKE ('%'||@q||'%'))
ORDER BY code;";

            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<WorkflowDefinitionRowDto>(
                new CommandDefinition(sql, new { tenantId, q = string.IsNullOrWhiteSpace(q) ? null : q.Trim() }, cancellationToken: ct));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar workflows.");
            return Array.Empty<WorkflowDefinitionRowDto>();
        }
    }

    public async Task<WorkflowDefinitionDetailsDto?> GetDefinitionAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
  id          AS ""Id"",
  code        AS ""Code"",
  name        AS ""Name"",
  description AS ""Description"",
  is_active   AS ""IsActive""
FROM ged.workflow_definition
WHERE tenant_id = @tenantId AND id = @id;";

            using var conn = await _db.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<WorkflowDefinitionDetailsDto>(
                new CommandDefinition(sql, new { tenantId, id }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar workflow.");
            return null;
        }
    }

    public async Task<IReadOnlyList<WorkflowStageRowDto>> ListStagesAsync(Guid tenantId, Guid workflowId, CancellationToken ct)
    {
        try
        {
            // workflow_stage não tem tenant_id no seu script; garantimos tenant via workflow_definition
            const string sql = @"
SELECT
  s.id            AS ""Id"",
  s.workflow_id   AS ""WorkflowId"",
  s.code          AS ""Code"",
  s.name          AS ""Name"",
  s.sort_order    AS ""SortOrder"",
  s.is_start      AS ""IsStart"",
  s.is_final      AS ""IsFinal"",
  s.required_role AS ""RequiredRole""
FROM ged.workflow_stage s
JOIN ged.workflow_definition w ON w.id = s.workflow_id AND w.tenant_id = @tenantId
WHERE s.workflow_id = @workflowId
ORDER BY s.sort_order, s.name;";

            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<WorkflowStageRowDto>(
                new CommandDefinition(sql, new { tenantId, workflowId }, cancellationToken: ct));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar etapas.");
            return Array.Empty<WorkflowStageRowDto>();
        }
    }

    public async Task<IReadOnlyList<WorkflowTransitionRowDto>> ListTransitionsAsync(Guid tenantId, Guid workflowId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
  t.id              AS ""Id"",
  t.workflow_id     AS ""WorkflowId"",
  t.from_stage_id   AS ""FromStageId"",
  t.to_stage_id     AS ""ToStageId"",
  t.name            AS ""Name"",
  t.requires_reason AS ""RequiresReason""
FROM ged.workflow_transition t
JOIN ged.workflow_definition w ON w.id = t.workflow_id AND w.tenant_id = @tenantId
WHERE t.workflow_id = @workflowId
ORDER BY t.name;";

            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<WorkflowTransitionRowDto>(
                new CommandDefinition(sql, new { tenantId, workflowId }, cancellationToken: ct));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar transições.");
            return Array.Empty<WorkflowTransitionRowDto>();
        }
    }

    // ✅ MÉTODO QUE ESTAVA FALTANDO (para compilar)
    public async Task<IReadOnlyList<WorkflowTransitionRowDto>> ListAvailableTransitionsAsync(
        Guid tenantId,
        Guid workflowId,
        Guid currentStageId,
        CancellationToken ct)
    {
        try
        {
            const string sql = @"
SELECT
  t.id              AS ""Id"",
  t.workflow_id     AS ""WorkflowId"",
  t.from_stage_id   AS ""FromStageId"",
  t.to_stage_id     AS ""ToStageId"",
  t.name            AS ""Name"",
  t.requires_reason AS ""RequiresReason""
FROM ged.workflow_transition t
JOIN ged.workflow_definition w ON w.id = t.workflow_id AND w.tenant_id = @tenantId
WHERE t.workflow_id = @workflowId
  AND t.from_stage_id = @currentStageId
ORDER BY t.name;";

            using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<WorkflowTransitionRowDto>(
                new CommandDefinition(sql, new { tenantId, workflowId, currentStageId }, cancellationToken: ct));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar transições disponíveis.");
            return Array.Empty<WorkflowTransitionRowDto>();
        }
    }
}
