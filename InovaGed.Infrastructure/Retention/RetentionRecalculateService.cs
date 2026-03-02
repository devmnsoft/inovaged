using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionRecalculateService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionRecalculateService> _logger;

    public RetentionRecalculateService(IDbConnectionFactory db, ILogger<RetentionRecalculateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public sealed record Result(int UpdatedDocs, int CreatedCases, int CreatedItems, Guid? CaseId);

    public async Task<Result> ExecuteAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1) Atualiza base/due/status/disposition conforme VIEW + regras PoC
            var updateDocsSql = @"
UPDATE ged.document d
SET
  retention_basis_at = v.base_date,
  retention_due_at   = v.final_end_at,
  retention_status   = CASE
      WHEN v.final_end_at IS NULL THEN 'UNKNOWN'
      WHEN v.final_end_at < now() THEN 'EXPIRED'
      WHEN v.final_end_at <= (now() + interval '30 days') THEN 'DUE_30'
      WHEN v.final_end_at <= (now() + interval '60 days') THEN 'DUE_60'
      WHEN v.final_end_at <= (now() + interval '90 days') THEN 'DUE_90'
      ELSE 'OK'
  END,
  disposition_status = CASE
      WHEN COALESCE(d.retention_hold,false) = true THEN COALESCE(d.disposition_status, 'HOLD')
      WHEN v.final_end_at < now() THEN
        CASE UPPER(COALESCE(v.final_destination::text,'REAVALIAR'))
          WHEN 'ELIMINAR'   THEN 'ELIMINATION_READY'
          WHEN 'TRANSFERIR' THEN 'TRANSFER_READY'
          WHEN 'REAVALIAR'  THEN 'REVIEW_REQUIRED'
          ELSE 'REVIEW_REQUIRED'
        END
      ELSE d.disposition_status
  END
FROM ged.vw_documents_retention_alerts v
WHERE v.tenant_id = @tenantId
  AND v.document_id = d.id
  AND d.tenant_id = @tenantId
  AND d.classification_id IS NOT NULL;";

            var updated = await conn.ExecuteAsync(
                new CommandDefinition(updateDocsSql, new { tenantId }, tx, cancellationToken: ct));

            // 2) Seleciona docs expirados e ainda sem caso
            var expiredSql = @"
SELECT d.id
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND COALESCE(d.retention_hold,false) = false
  AND d.retention_status = 'EXPIRED'
  AND d.disposition_case_id IS NULL;";

            var expiredDocIds = (await conn.QueryAsync<Guid>(
                new CommandDefinition(expiredSql, new { tenantId }, tx, cancellationToken: ct))).ToList();

            if (expiredDocIds.Count == 0)
            {
                await tx.CommitAsync(ct);
                return new Result(updated, 0, 0, null);
            }

            // 3) Cria 1 caso OPEN (PoC) e itens para esses docs
            // Ajuste campos/colunas conforme seu schema real do retention_case
            var createCaseSql = @"
INSERT INTO ged.retention_case (tenant_id, case_no, status, created_at, created_by)
VALUES (@tenantId,
        COALESCE((SELECT max(case_no) FROM ged.retention_case WHERE tenant_id=@tenantId),0) + 1,
        'OPEN',
        now(),
        @userId)
RETURNING id;";

            var caseId = await conn.ExecuteScalarAsync<Guid>(
                new CommandDefinition(createCaseSql, new { tenantId, userId }, tx, cancellationToken: ct));

            // Itens (um por documento)
            var createItemSql = @"
INSERT INTO ged.retention_case_item
(tenant_id, case_id, document_id, suggested_destination, status, created_at, created_by)
SELECT
  d.tenant_id,
  @caseId,
  d.id,
  COALESCE(d.disposition_status,'REVIEW_REQUIRED'),
  'PENDING',
  now(),
  @userId
FROM ged.document d
WHERE d.tenant_id = @tenantId
  AND d.id = ANY(@docIds);";

            var items = await conn.ExecuteAsync(
                new CommandDefinition(createItemSql, new { tenantId, userId, caseId, docIds = expiredDocIds.ToArray() }, tx, cancellationToken: ct));

            // 4) Amarra docs no caso para não duplicar
            var linkDocsSql = @"
UPDATE ged.document
SET disposition_case_id = @caseId
WHERE tenant_id = @tenantId
  AND id = ANY(@docIds);";

            await conn.ExecuteAsync(
                new CommandDefinition(linkDocsSql, new { tenantId, caseId, docIds = expiredDocIds.ToArray() }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new Result(updated, 1, items, caseId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Retention recalculation failed (tenant={TenantId})", tenantId);
            throw;
        }
    }
}