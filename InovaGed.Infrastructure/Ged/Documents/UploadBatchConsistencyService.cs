using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Domain.Primitives;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class UploadBatchConsistencyService : IUploadBatchConsistencyService
{
    private readonly IDbConnectionFactory _db;

    public UploadBatchConsistencyService(IDbConnectionFactory db) => _db = db;

    public async Task<Result<UploadBatchConsistencyResult>> RecalculateAsync(Guid tenantId, Guid batchId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var result = await conn.QuerySingleOrDefaultAsync<UploadBatchConsistencyResult>(new CommandDefinition("""
WITH c AS (
  SELECT count(*)::int total,
         count(*) FILTER (WHERE status='COMPLETED')::int success,
         count(*) FILTER (WHERE status IN ('ERROR','ABORTED','RETRYABLE'))::int failed,
         count(*) FILTER (WHERE status IN ('SKIPPED','DUPLICATE'))::int skipped,
         count(*) FILTER (WHERE status IN ('PENDING','RECEIVING','SAVED','DOCUMENT_CREATED','QUEUED'))::int pending,
         count(*) FILTER (WHERE status='CANCELLED')::int cancelled
  FROM ged.upload_batch_item
  WHERE tenant_id=@tenantId AND batch_id=@batchId AND coalesce(reg_status,'A')='A'
), u AS (
  UPDATE ged.upload_batch b
  SET total_files = CASE WHEN c.total > 0 THEN c.total ELSE b.total_files END,
      success_files = c.success,
      failed_files = c.failed,
      skipped_files = c.skipped,
      status = CASE
        WHEN c.cancelled > 0 AND c.pending = 0 AND c.success = 0 AND c.failed = 0 THEN 'CANCELLED'
        WHEN c.pending > 0 THEN 'PROCESSING'
        WHEN c.failed > 0 AND (c.success > 0 OR c.skipped > 0) THEN 'PARTIAL_ERROR'
        WHEN c.failed > 0 THEN 'ERROR'
        ELSE 'COMPLETED'
      END,
      finished_at = CASE WHEN c.pending = 0 THEN coalesce(b.finished_at, now()) ELSE b.finished_at END,
      updated_at = now()
  FROM c
  WHERE b.tenant_id=@tenantId AND b.id=@batchId AND coalesce(b.reg_status,'A')='A'
  RETURNING b.id AS BatchId, b.total_files AS TotalFiles, b.success_files AS SuccessFiles, b.failed_files AS FailedFiles, b.skipped_files AS SkippedFiles, b.status, b.finished_at AS FinishedAt
)
SELECT * FROM u;
""", new { tenantId, batchId }, cancellationToken: ct));
        return result is null ? Result<UploadBatchConsistencyResult>.Fail("NOT_FOUND", "Lote não encontrado.") : Result<UploadBatchConsistencyResult>.Ok(result);
    }
}
