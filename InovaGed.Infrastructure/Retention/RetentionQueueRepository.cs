using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionQueueRepository : IRetentionQueueRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionQueueRepository> _logger;

    public RetentionQueueRepository(IDbConnectionFactory db, ILogger<RetentionQueueRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetentionRuleRow>> ListRulesAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            var sql = """
SELECT id,
       class_code AS classcode,
       start_event AS startevent,
       current_days AS currentdays,
       intermediate_days AS intermediatedays,
       final_destination AS finaldestination,
       notes
FROM ged.retention_rule
WHERE tenant_id=@TenantId AND reg_status='A'
ORDER BY class_code;
""";

            var rows = await con.QueryAsync<RetentionRuleRow>(new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListRulesAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<Guid> UpsertRuleAsync(Guid tenantId, Guid? id, RetentionRuleRow rule, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var rid = id ?? Guid.NewGuid();

            var sql = """
INSERT INTO ged.retention_rule(id, tenant_id, class_code, start_event, current_days, intermediate_days, final_destination, notes)
VALUES (@Id, @TenantId, @ClassCode, @StartEvent, @CurrentDays, @IntermediateDays, @FinalDestination, @Notes)
ON CONFLICT (tenant_id, class_code) DO UPDATE SET
  start_event=EXCLUDED.start_event,
  current_days=EXCLUDED.current_days,
  intermediate_days=EXCLUDED.intermediate_days,
  final_destination=EXCLUDED.final_destination,
  notes=EXCLUDED.notes,
  reg_date=now(),
  reg_status='A'
RETURNING id;
""";

            var ret = await con.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                Id = rid,
                TenantId = tenantId,
                ClassCode = (rule.ClassCode ?? "").Trim(),
                StartEvent = (rule.StartEvent ?? "CREATED").Trim().ToUpperInvariant(),
                CurrentDays = rule.CurrentDays,
                IntermediateDays = rule.IntermediateDays,
                FinalDestination = (rule.FinalDestination ?? "ELIMINAR").Trim().ToUpperInvariant(),
                Notes = rule.Notes
            }, cancellationToken: ct));

            return ret;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpsertRuleAsync failed Tenant={Tenant} Class={Class}", tenantId, rule.ClassCode);
            throw;
        }
    }

    /// <summary>
    /// Gera/atualiza a fila de temporalidade.
    /// - Usa documentos ativos (status <> 'DELETED')
    /// - Resolve class_code pelo classification_plan (id = document.classification_id)
    /// - Aplica regra por class_code (retention_rule.class_code)
    /// - due_at = document.created_at + (current_days+intermediate_days)
    /// - Insere PENDING se não existir PENDING/IN_TERM ativo
    /// </summary>
    public async Task<int> GenerateQueueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            var sql = """
WITH rules AS (
  SELECT class_code, (current_days + intermediate_days) AS total_days
  FROM ged.retention_rule
  WHERE tenant_id=@TenantId AND reg_status='A'
),
docs AS (
  SELECT
    d.id AS document_id,
    d.created_at,
    cp.code AS class_code
  FROM ged.document d
  JOIN ged.classification_plan cp
    ON cp.tenant_id = d.tenant_id
   AND cp.id = d.classification_id
   AND cp.is_active = true
  WHERE d.tenant_id=@TenantId
    AND d.status <> 'DELETED'
),
due AS (
  SELECT
    docs.document_id,
    docs.class_code,
    (docs.created_at + (rules.total_days || ' days')::interval) AS due_at
  FROM docs
  JOIN rules ON rules.class_code = docs.class_code
)
INSERT INTO ged.retention_queue(id, tenant_id, document_id, class_code, due_at, status, generated_at)
SELECT gen_random_uuid(), @TenantId, due.document_id, due.class_code, due.due_at, 'PENDING', @Now
FROM due
WHERE due.due_at IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM ged.retention_queue q
      WHERE q.tenant_id=@TenantId AND q.document_id=due.document_id
        AND q.status IN ('PENDING','IN_TERM') AND q.reg_status='A'
  );
""";

            var affected = await con.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, Now = now }, cancellationToken: ct));
            return affected;
        }
        catch (PostgresException pg) when (pg.SqlState is "42P01" or "42703")
        {
            _logger.LogError(pg, "GenerateQueueAsync: schema mismatch (tabela/coluna). Ajuste SQL conforme bdgedprd.sql.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateQueueAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<RetentionQueueRow>> ListQueueAsync(Guid tenantId, string bucket, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            var now = DateTimeOffset.UtcNow;

            string where = (bucket ?? "overdue").Trim().ToLowerInvariant() switch
            {
                "overdue" => "q.due_at < @Now",
                "due30" => "q.due_at >= @Now AND q.due_at < (@Now + interval '30 days')",
                "due60" => "q.due_at >= @Now AND q.due_at < (@Now + interval '60 days')",
                "due90" => "q.due_at >= @Now AND q.due_at < (@Now + interval '90 days')",
                _ => "1=1"
            };

            // ✅ Agora traz dados do documento + plano para a view funcionar (Code/Title/Class...)
            var sql = $"""
SELECT
  q.id AS id,
  q.document_id AS documentid,

  COALESCE(d.code,'') AS code,
  COALESCE(d.title,'') AS title,

  COALESCE(cp.code,'') AS classificationcode,
  COALESCE(cp.name,'') AS classificationname,

  q.due_at AS dueat,
  CASE
    WHEN cp.code IS NULL OR cp.code = '' THEN 'SEM_CLASSIFICACAO'
    WHEN q.due_at IS NULL THEN 'SEM_CLASSIFICACAO'
    WHEN q.due_at < now() THEN 'OVERDUE'
    WHEN q.due_at < (now() + interval '30 days') THEN 'DUE_SOON'
    ELSE 'OK'
  END AS status,
  CAST(EXTRACT(DAY FROM (q.due_at - now())) AS int) AS daystodue,

  cp.final_destination::text AS suggesteddestination,

  q.generated_at AS generatedat
FROM ged.retention_queue q
JOIN ged.document d
  ON d.tenant_id = q.tenant_id
 AND d.id = q.document_id
 AND d.status <> 'DELETED'
LEFT JOIN ged.classification_plan cp
  ON cp.tenant_id = d.tenant_id
 AND cp.id = d.classification_id
 AND cp.is_active = true
WHERE q.tenant_id=@TenantId AND q.reg_status='A'
  AND q.status='PENDING'
  AND {where}
ORDER BY q.due_at ASC NULLS LAST, d.title;
""";

            var rows = await con.QueryAsync<RetentionQueueRow>(
                new CommandDefinition(sql, new { TenantId = tenantId, Now = now }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListQueueAsync failed Tenant={Tenant} Bucket={Bucket}", tenantId, bucket);
            throw;
        }
    }

    public async Task MarkInTermAsync(Guid tenantId, IEnumerable<Guid> queueIds, CancellationToken ct)
    {
        var ids = queueIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
        if (ids.Length == 0) return;

        try
        {
            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition("""
UPDATE ged.retention_queue
SET status='IN_TERM'
WHERE tenant_id=@TenantId AND id=ANY(@Ids) AND reg_status='A';
""", new { TenantId = tenantId, Ids = ids }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkInTermAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task MarkDoneForDocumentsAsync(Guid tenantId, IEnumerable<Guid> documentIds, CancellationToken ct)
    {
        var ids = documentIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
        if (ids.Length == 0) return;

        try
        {
            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition("""
UPDATE ged.retention_queue
SET status='DONE'
WHERE tenant_id=@TenantId AND document_id=ANY(@Ids) AND reg_status='A';
""", new { TenantId = tenantId, Ids = ids }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkDoneForDocumentsAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }
}