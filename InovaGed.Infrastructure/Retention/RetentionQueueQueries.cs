using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionQueueQueries : IRetentionQueueQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionQueueQueries> _logger;

    public RetentionQueueQueries(IDbConnectionFactory db, ILogger<RetentionQueueQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetentionQueueRow>> ListAsync(Guid tenantId, RetentionQueueFilter filter, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            var sql = """
WITH base AS (
  SELECT
    d.id AS document_id,
    d.code AS doc_code,
    d.title AS doc_title,

    -- class info vem do plano (join por classification_id)
    cp.code AS class_code,
    cp.name AS class_name,
    cp.final_destination::text AS suggested_destination,

    -- vencimento: preferimos o campo do documento, se existir
    COALESCE(d.retention_due_at, q.due_at) AS due_at

  FROM ged.document d

  LEFT JOIN ged.retention_queue q
    ON q.tenant_id = d.tenant_id
   AND q.document_id = d.id
   AND q.reg_status = 'A'
   AND q.status IN ('PENDING','IN_TERM')

  LEFT JOIN ged.classification_plan cp
    ON cp.tenant_id = d.tenant_id
   AND cp.id = d.classification_id
   AND cp.is_active = true

  WHERE d.tenant_id = @TenantId
    AND d.status <> 'DELETED'
),
calc AS (
  SELECT
    b.document_id,
    b.doc_code,
    b.doc_title,
    b.class_code,
    b.class_name,
    b.suggested_destination,
    b.due_at,
    CASE
      WHEN b.class_code IS NULL OR b.class_code = '' THEN 'SEM_CLASSIFICACAO'
      WHEN b.due_at IS NULL THEN 'SEM_CLASSIFICACAO'
      WHEN b.due_at < now() THEN 'OVERDUE'
      WHEN b.due_at < (now() + interval '30 days') THEN 'DUE_SOON'
      ELSE 'OK'
    END AS status,
    CASE
      WHEN b.due_at IS NULL THEN NULL
      ELSE CAST(EXTRACT(DAY FROM (b.due_at - now())) AS int)
    END AS days_to_due
  FROM base b
)
SELECT
  c.document_id AS documentid,
  COALESCE(c.doc_code,'') AS code,
  COALESCE(c.doc_title,'') AS title,
  COALESCE(c.class_code,'') AS classificationcode,
  COALESCE(c.class_name,'') AS classificationname,
  c.due_at AS dueat,
  c.status AS status,
  c.days_to_due AS daystodue,
  c.suggested_destination AS suggesteddestination
FROM calc c
WHERE 1=1
""";

            var dp = new DynamicParameters();
            dp.Add("TenantId", tenantId);

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                sql += " AND c.status = @Status";
                dp.Add("Status", filter.Status.Trim().ToUpperInvariant());
            }

            if (filter.DueUntil.HasValue)
            {
                sql += " AND c.due_at IS NOT NULL AND c.due_at <= @DueUntil";
                dp.Add("DueUntil", filter.DueUntil.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Q))
            {
                sql += " AND (COALESCE(c.doc_code,'') ILIKE @Q OR COALESCE(c.doc_title,'') ILIKE @Q OR COALESCE(c.class_code,'') ILIKE @Q OR COALESCE(c.class_name,'') ILIKE @Q)";
                dp.Add("Q", "%" + filter.Q.Trim() + "%");
            }

            sql += " ORDER BY c.due_at NULLS LAST, c.doc_title;";

            var rows = await con.QueryAsync<RetentionQueueRow>(new CommandDefinition(sql, dp, cancellationToken: ct));
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionQueueQueries.ListAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<RetentionQueueRow>> ListByIdsAsync(Guid tenantId, Guid[] documentIds, CancellationToken ct)
    {
        if (documentIds == null || documentIds.Length == 0)
            return Array.Empty<RetentionQueueRow>();

        try
        {
            await using var con = await _db.OpenAsync(ct);

            var sql = """
WITH base AS (
  SELECT
    d.id AS document_id,
    d.code AS doc_code,
    d.title AS doc_title,
    cp.code AS class_code,
    cp.name AS class_name,
    cp.final_destination::text AS suggested_destination,
    COALESCE(d.retention_due_at, q.due_at) AS due_at
  FROM ged.document d
  LEFT JOIN ged.retention_queue q
    ON q.tenant_id = d.tenant_id
   AND q.document_id = d.id
   AND q.reg_status = 'A'
   AND q.status IN ('PENDING','IN_TERM')
  LEFT JOIN ged.classification_plan cp
    ON cp.tenant_id = d.tenant_id
   AND cp.id = d.classification_id
   AND cp.is_active = true
  WHERE d.tenant_id=@TenantId
    AND d.status <> 'DELETED'
    AND d.id = ANY(@Ids)
),
calc AS (
  SELECT
    b.*,
    CASE
      WHEN b.class_code IS NULL OR b.class_code = '' THEN 'SEM_CLASSIFICACAO'
      WHEN b.due_at IS NULL THEN 'SEM_CLASSIFICACAO'
      WHEN b.due_at < now() THEN 'OVERDUE'
      WHEN b.due_at < (now() + interval '30 days') THEN 'DUE_SOON'
      ELSE 'OK'
    END AS status,
    CASE
      WHEN b.due_at IS NULL THEN NULL
      ELSE CAST(EXTRACT(DAY FROM (b.due_at - now())) AS int)
    END AS days_to_due
  FROM base b
)
SELECT
  c.document_id AS documentid,
  COALESCE(c.doc_code,'') AS code,
  COALESCE(c.doc_title,'') AS title,
  COALESCE(c.class_code,'') AS classificationcode,
  COALESCE(c.class_name,'') AS classificationname,
  c.due_at AS dueat,
  c.status AS status,
  c.days_to_due AS daystodue,
  c.suggested_destination AS suggesteddestination
FROM calc c
ORDER BY c.due_at NULLS LAST, c.doc_title;
""";

            var ids = documentIds.Distinct().ToArray();

            var rows = await con.QueryAsync<RetentionQueueRow>(
                new CommandDefinition(sql, new { TenantId = tenantId, Ids = ids }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionQueueQueries.ListByIdsAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }
}