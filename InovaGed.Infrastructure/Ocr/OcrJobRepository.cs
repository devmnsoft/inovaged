using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrJobRepository : IOcrJobRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrJobRepository> _logger;

    // Ajuste se quiser via config
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public OcrJobRepository(IDbConnectionFactory db, ILogger<OcrJobRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<long> EnqueueAsync(
        Guid tenantId,
        Guid documentVersionId,
        Guid? requestedBy,
        bool invalidateDigitalSignatures,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.ocr_job
  (tenant_id, document_version_id, status, requested_by, invalidate_digital_signatures)
VALUES
  (@tenantId, @documentVersionId, 'PENDING'::ged.ocr_status_enum, @requestedBy, @invalidate)
RETURNING id;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var id = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, new
                {
                    tenantId,
                    documentVersionId,
                    requestedBy,
                    invalidate = invalidateDigitalSignatures
                }, cancellationToken: ct));

            _logger.LogInformation(
                "OCR job enfileirado. JobId={JobId}, Tenant={TenantId}, Version={VersionId}, RequestedBy={RequestedBy}",
                id, tenantId, documentVersionId, requestedBy);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enfileirar OCR. Tenant={TenantId}, Version={VersionId}", tenantId, documentVersionId);
            throw;
        }
    }

    public async Task<OcrJobDto?> DequeueAndMarkProcessingAsync(CancellationToken ct)
    {
        const string sql = @"
WITH cte AS (
  SELECT id
  FROM ged.ocr_job
  WHERE
    status = 'PENDING'::ged.ocr_status_enum
    OR (status = 'PROCESSING'::ged.ocr_status_enum AND lease_expires_at IS NOT NULL AND lease_expires_at < now())
  ORDER BY requested_at
  FOR UPDATE SKIP LOCKED
  LIMIT 1
)
UPDATE ged.ocr_job j
SET status = 'PROCESSING'::ged.ocr_status_enum,
    started_at = COALESCE(j.started_at, now()),
    lease_expires_at = now() + (@leaseSeconds || ' seconds')::interval,
    error_message = null
FROM cte
WHERE j.id = cte.id
RETURNING
  j.id                            AS Id,
  j.tenant_id                     AS TenantId,
  j.document_version_id           AS DocumentVersionId,
  j.requested_by                  AS RequestedBy,
  j.invalidate_digital_signatures AS InvalidateDigitalSignatures;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var job = await conn.QuerySingleOrDefaultAsync<OcrJobDto>(
                new CommandDefinition(sql, new
                {
                    leaseSeconds = (int)LeaseDuration.TotalSeconds
                }, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no DequeueAndMarkProcessingAsync");
            throw;
        }
    }

    public async Task RenewLeaseAsync(long jobId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET lease_expires_at = now() + (@leaseSeconds || ' seconds')::interval
WHERE id = @jobId
  AND status = 'PROCESSING'::ged.ocr_status_enum;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                jobId,
                leaseSeconds = (int)LeaseDuration.TotalSeconds
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao renovar lease. JobId={JobId}", jobId);
            throw;
        }
    }

    public async Task MarkCompletedAsync(long jobId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'COMPLETED'::ged.ocr_status_enum,
    finished_at = now(),
    lease_expires_at = null,
    error_message = null
WHERE id = @jobId;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao marcar COMPLETED. JobId={JobId}", jobId);
            throw;
        }
    }

    public async Task MarkErrorAsync(long jobId, string errorMessage, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'ERROR'::ged.ocr_status_enum,
    finished_at = now(),
    lease_expires_at = null,
    error_message = @error
WHERE id = @jobId;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId, error = errorMessage }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao marcar ERROR. JobId={JobId}", jobId);
            throw;
        }
    }

    // Usado pelo OcrWorkerHostedService (se você ainda usar)
    public async Task<OcrJobLease?> LeaseNextAsync(TimeSpan leaseTime, CancellationToken ct)
    {
        const string sql = @"
WITH cte AS (
  SELECT id
  FROM ged.ocr_job
  WHERE status = 'PENDING'::ged.ocr_status_enum
    AND (lease_expires_at IS NULL OR lease_expires_at < now())
  ORDER BY requested_at
  LIMIT 1
  FOR UPDATE SKIP LOCKED
)
UPDATE ged.ocr_job j
SET status = 'PROCESSING'::ged.ocr_status_enum,
    started_at = COALESCE(started_at, now()),
    lease_expires_at = now() + (@LeaseSeconds || ' seconds')::interval,
    error_message = null
FROM cte
WHERE j.id = cte.id
RETURNING j.id, j.tenant_id, j.document_version_id, j.requested_by, j.invalidate_digital_signatures;
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var row = await conn.QueryFirstOrDefaultAsync(
                new CommandDefinition(sql, new { LeaseSeconds = (int)leaseTime.TotalSeconds }, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);

            if (row is null) return null;

            return new OcrJobLease(
                JobId: (long)row.id,
                TenantId: (Guid)row.tenant_id,
                DocumentVersionId: (Guid)row.document_version_id,
                InvalidateDigitalSignatures: (bool)row.invalidate_digital_signatures
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no LeaseNextAsync");
            throw;
        }
    }
}