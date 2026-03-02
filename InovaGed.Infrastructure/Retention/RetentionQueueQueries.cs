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

            // ==========================
            // AJUSTE AQUI (DOCUMENTOS)
            // ==========================
            // Troque "ged.document" e colunas:
            // - d.id (uuid)
            // - d.code (text)  -> pode ser numero/protocolo
            // - d.title (text)
            // - d.class_code (text) -> código de classificação
            // - d.created_at (timestamptz)
            //
            // Se seu schema é outro (documents/document_versions/folders),
            // adapte somente esse bloco SELECT/JOINS.
            // ==========================

            var sql = """
            WITH base AS (
              SELECT
                d.id AS document_id,
                d.code AS doc_code,
                d.title AS doc_title,
                d.class_code AS class_code,
                q.due_at AS due_at
              FROM ged.document d
              LEFT JOIN ged.retention_queue q
                ON q.tenant_id = d.tenant_id
               AND q.document_id = d.id
               AND q.reg_status = 'A'
               AND q.status IN ('PENDING','IN_TERM')
              WHERE d.tenant_id=@TenantId AND d.reg_status='A'
            ),
            calc AS (
              SELECT
                b.document_id,
                b.doc_code,
                b.doc_title,
                b.class_code,
                b.due_at,
                CASE
                  WHEN b.class_code IS NULL OR b.class_code='' THEN 'SEM_CLASSIFICACAO'
                  WHEN b.due_at IS NULL THEN 'SEM_CLASSIFICACAO'
                  WHEN b.due_at < now() THEN 'OVERDUE'
                  WHEN b.due_at < (now() + interval '30 days') THEN 'DUE_SOON'
                  ELSE 'OK'
                END AS status,
                CAST(EXTRACT(DAY FROM (b.due_at - now())) AS int) AS days_to_due
              FROM base b
            )
            SELECT
              c.document_id AS documentid,
              COALESCE(c.doc_code,'') AS code,
              COALESCE(c.doc_title,'') AS title,
              COALESCE(c.class_code,'') AS classificationcode,
              ''::text AS classificationname,
              c.due_at AS dueat,
              c.status AS status,
              c.days_to_due AS daystodue
            FROM calc c
            WHERE 1=1
            """;

            // filtros dinâmicos seguros
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
                sql += " AND (COALESCE(c.doc_code,'') ILIKE @Q OR COALESCE(c.doc_title,'') ILIKE @Q OR COALESCE(c.class_code,'') ILIKE @Q)";
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
        if (documentIds == null || documentIds.Length == 0) return Array.Empty<RetentionQueueRow>();

        try
        {
            await using var con = await _db.OpenAsync(ct);

            var sql = """
            WITH base AS (
              SELECT
                d.id AS document_id,
                d.code AS doc_code,
                d.title AS doc_title,
                d.class_code AS class_code,
                q.due_at AS due_at
              FROM ged.document d
              LEFT JOIN ged.retention_queue q
                ON q.tenant_id = d.tenant_id
               AND q.document_id = d.id
               AND q.reg_status = 'A'
               AND q.status IN ('PENDING','IN_TERM')
              WHERE d.tenant_id=@TenantId AND d.reg_status='A'
                AND d.id = ANY(@Ids)
            )
            SELECT
              b.document_id AS documentid,
              COALESCE(b.doc_code,'') AS code,
              COALESCE(b.doc_title,'') AS title,
              COALESCE(b.class_code,'') AS classificationcode,
              ''::text AS classificationname,
              b.due_at AS dueat,
              CASE
                WHEN b.class_code IS NULL OR b.class_code='' THEN 'SEM_CLASSIFICACAO'
                WHEN b.due_at IS NULL THEN 'SEM_CLASSIFICACAO'
                WHEN b.due_at < now() THEN 'OVERDUE'
                WHEN b.due_at < (now() + interval '30 days') THEN 'DUE_SOON'
                ELSE 'OK'
              END AS status,
              CAST(EXTRACT(DAY FROM (b.due_at - now())) AS int) AS daystodue
            FROM base b
            ORDER BY b.due_at NULLS LAST, b.doc_title;
            """;

            var rows = await con.QueryAsync<RetentionQueueRow>(new CommandDefinition(sql,
                new { TenantId = tenantId, Ids = documentIds.Distinct().ToArray() },
                cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionQueueQueries.ListByIdsAsync failed Tenant={Tenant}", tenantId);
            throw;
        }
    }
}