using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class GedProcessingJobRepository : IGedProcessingJobRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<GedProcessingJobRepository> _logger;

    public GedProcessingJobRepository(IDbConnectionFactory db, ILogger<GedProcessingJobRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnqueueAsync(Guid tenantId, Guid? documentId, Guid? documentVersionId, Guid? uploadBatchId, Guid? uploadBatchItemId, string jobType, int priority, CancellationToken ct)
    {
        const string sql = """
INSERT INTO ged.processing_job (tenant_id, document_id, document_version_id, upload_batch_id, upload_batch_item_id, job_type, status, priority)
VALUES (@tenantId, @documentId, @documentVersionId, @uploadBatchId, @uploadBatchItemId, @jobType, 'PENDING', @priority)
ON CONFLICT DO NOTHING;
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, documentVersionId, uploadBatchId, uploadBatchItemId, jobType, priority }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex, "Tabela ged.processing_job ausente; job {JobType} não enfileirado. Tenant={TenantId} Version={VersionId}", jobType, tenantId, documentVersionId);
        }
    }

    public async Task<IReadOnlyList<GedProcessingJobDto>> DequeueAsync(string workerId, int take, CancellationToken ct)
    {
        const string sql = """
WITH picked AS (
  SELECT id
  FROM ged.processing_job
  WHERE reg_status='A'
    AND status='PENDING'
    AND (next_attempt_at IS NULL OR next_attempt_at <= now())
  ORDER BY priority ASC, created_at ASC
  FOR UPDATE SKIP LOCKED
  LIMIT @take
)
UPDATE ged.processing_job j
SET status='PROCESSING', locked_by=@workerId, locked_at=now(), started_at=COALESCE(started_at, now()), attempt_count=attempt_count+1, updated_at=now()
FROM picked
WHERE j.id=picked.id
RETURNING j.id, j.tenant_id AS TenantId, j.document_id AS DocumentId, j.document_version_id AS DocumentVersionId,
          j.upload_batch_id AS UploadBatchId, j.upload_batch_item_id AS UploadBatchItemId,
          j.job_type AS JobType, j.attempt_count AS AttemptCount, j.max_attempts AS MaxAttempts;
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return (await conn.QueryAsync<GedProcessingJobDto>(new CommandDefinition(sql, new { workerId, take }, cancellationToken: ct))).AsList();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex, "Tabela ged.processing_job ausente; worker GED aguardará migrations.");
            return Array.Empty<GedProcessingJobDto>();
        }
    }

    public async Task CompleteAsync(Guid tenantId, Guid jobId, CancellationToken ct) => await ExecuteAsync("UPDATE ged.processing_job SET status='COMPLETED', finished_at=now(), locked_by=NULL, locked_at=NULL, updated_at=now() WHERE tenant_id=@tenantId AND id=@jobId;", new { tenantId, jobId }, ct);

    public async Task FailAsync(Guid tenantId, Guid jobId, string errorMessage, TimeSpan retryDelay, CancellationToken ct) => await ExecuteAsync("""
UPDATE ged.processing_job
SET status=CASE WHEN attempt_count >= max_attempts THEN 'FAILED' ELSE 'PENDING' END,
    error_message=@errorMessage,
    next_attempt_at=CASE WHEN attempt_count >= max_attempts THEN NULL ELSE now() + (@retrySeconds || ' seconds')::interval END,
    locked_by=NULL, locked_at=NULL, finished_at=CASE WHEN attempt_count >= max_attempts THEN now() ELSE finished_at END, updated_at=now()
WHERE tenant_id=@tenantId AND id=@jobId;
""", new { tenantId, jobId, errorMessage = Truncate(errorMessage), retrySeconds = (int)Math.Max(5, retryDelay.TotalSeconds) }, ct);

    public async Task CancelAsync(Guid tenantId, Guid jobId, string reason, CancellationToken ct) => await ExecuteAsync("UPDATE ged.processing_job SET status='CANCELLED', error_message=@reason, finished_at=now(), locked_by=NULL, locked_at=NULL, updated_at=now() WHERE tenant_id=@tenantId AND id=@jobId;", new { tenantId, jobId, reason = Truncate(reason) }, ct);

    private async Task ExecuteAsync(string sql, object args, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, args, cancellationToken: ct));
    }

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}
