using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrJobRepository : IOcrJobRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrJobRepository> _logger;

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
        await using var conn = await _db.OpenAsync(ct);

        if (!invalidateDigitalSignatures)
        {
            const string existingSql = @"
SELECT id
FROM ged.ocr_job
WHERE tenant_id = @tenantId
  AND document_version_id = @documentVersionId
  AND status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum, 'COMPLETED'::ged.ocr_status_enum)
ORDER BY requested_at DESC
LIMIT 1;";

            var existingId = await conn.ExecuteScalarAsync<long?>(
                new CommandDefinition(
                    existingSql,
                    new { tenantId, documentVersionId },
                    cancellationToken: ct));

            if (existingId.HasValue)
            {
                _logger.LogInformation(
                    "OCR não enfileirado porque já existe job ativo/concluído. JobId={JobId}, Tenant={TenantId}, Version={VersionId}",
                    existingId.Value,
                    tenantId,
                    documentVersionId);

                return existingId.Value;
            }
        }

        const string sql = @"
INSERT INTO ged.ocr_job
  (tenant_id, document_version_id, status, requested_by, invalidate_digital_signatures)
VALUES
  (@tenantId, @documentVersionId, 'PENDING'::ged.ocr_status_enum, @requestedBy, @invalidate)
ON CONFLICT ON CONSTRAINT ocr_job_pkey DO NOTHING
RETURNING id;";

        try
        {
            var id = await conn.ExecuteScalarAsync<long?>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        tenantId,
                        documentVersionId,
                        requestedBy,
                        invalidate = invalidateDigitalSignatures
                    },
                    cancellationToken: ct));

            if (id.HasValue)
            {
                _logger.LogInformation(
                    "OCR job enfileirado. JobId={JobId}, Tenant={TenantId}, Version={VersionId}, RequestedBy={RequestedBy}",
                    id.Value,
                    tenantId,
                    documentVersionId,
                    requestedBy);

                return id.Value;
            }

            const string fallbackSql = @"
SELECT id
FROM ged.ocr_job
WHERE tenant_id = @tenantId
  AND document_version_id = @documentVersionId
  AND status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
ORDER BY requested_at DESC
LIMIT 1;";

            var fallbackId = await conn.ExecuteScalarAsync<long?>(
                new CommandDefinition(
                    fallbackSql,
                    new { tenantId, documentVersionId },
                    cancellationToken: ct));

            if (fallbackId.HasValue)
                return fallbackId.Value;

            const string insertDirectSql = @"
INSERT INTO ged.ocr_job
  (tenant_id, document_version_id, status, requested_by, invalidate_digital_signatures)
VALUES
  (@tenantId, @documentVersionId, 'PENDING'::ged.ocr_status_enum, @requestedBy, @invalidate)
RETURNING id;";

            return await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    insertDirectSql,
                    new
                    {
                        tenantId,
                        documentVersionId,
                        requestedBy,
                        invalidate = invalidateDigitalSignatures
                    },
                    cancellationToken: ct));
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
    OR (
        status = 'PROCESSING'::ged.ocr_status_enum
        AND lease_expires_at IS NOT NULL
        AND lease_expires_at < now()
    )
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
  j.id                            AS ""Id"",
  j.tenant_id                     AS ""TenantId"",
  j.document_version_id           AS ""DocumentVersionId"",
  j.requested_by                  AS ""RequestedBy"",
  j.invalidate_digital_signatures AS ""InvalidateDigitalSignatures"";";

        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var job = await conn.QuerySingleOrDefaultAsync<OcrJobDto>(
            new CommandDefinition(
                sql,
                new { leaseSeconds = (int)LeaseDuration.TotalSeconds },
                transaction: tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);

        return job;
    }

    public async Task RenewLeaseAsync(long jobId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET lease_expires_at = now() + (@leaseSeconds || ' seconds')::interval
WHERE id = @jobId
  AND status = 'PROCESSING'::ged.ocr_status_enum;";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    jobId,
                    leaseSeconds = (int)LeaseDuration.TotalSeconds
                },
                cancellationToken: ct));
    }

    public async Task MarkCompletedAsync(long jobId, CancellationToken ct)
    {
        await MarkCompletedAsync(jobId, null, null, ct);
    }

    public async Task MarkCompletedAsync(long jobId, Guid? documentId, Guid? resultVersionId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'COMPLETED'::ged.ocr_status_enum,
    finished_at = now(),
    lease_expires_at = null,
    error_message = null,
    result_document_id = COALESCE(@documentId, result_document_id),
    result_version_id = COALESCE(@resultVersionId, result_version_id)
WHERE id = @jobId;";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { jobId, documentId, resultVersionId },
                cancellationToken: ct));
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

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { jobId, error = errorMessage },
                cancellationToken: ct));
    }

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
RETURNING
    j.id,
    j.tenant_id,
    j.document_version_id,
    j.requested_by,
    j.invalidate_digital_signatures;";

        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QueryFirstOrDefaultAsync(
            new CommandDefinition(
                sql,
                new { LeaseSeconds = (int)leaseTime.TotalSeconds },
                transaction: tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);

        if (row is null) return null;

        return new OcrJobLease(
            JobId: (long)row.id,
            TenantId: (Guid)row.tenant_id,
            DocumentVersionId: (Guid)row.document_version_id,
            InvalidateDigitalSignatures: (bool)row.invalidate_digital_signatures
        );
    }

    public async Task<OcrJobStatusDto?> GetLatestByVersionIdAsync(Guid tenantId, Guid documentVersionId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  j.document_version_id AS ""VersionId"",
  j.status::text        AS ""StatusText"",
  j.id                  AS ""JobId"",
  j.requested_at        AS ""RequestedAt"",
  j.started_at          AS ""StartedAt"",
  j.finished_at         AS ""FinishedAt"",
  j.error_message       AS ""ErrorMessage"",
  j.invalidate_digital_signatures AS ""InvalidateDigitalSignatures""
FROM ged.ocr_job j
WHERE j.tenant_id = @tenantId
  AND j.document_version_id = @documentVersionId
ORDER BY j.requested_at DESC
LIMIT 1;";

        await using var conn = await _db.OpenAsync(ct);

        var row = await conn.QueryFirstOrDefaultAsync(
            new CommandDefinition(
                sql,
                new { tenantId, documentVersionId },
                cancellationToken: ct));

        if (row is null) return null;

        string statusText = row.StatusText;

        if (!Enum.TryParse<OcrStatusEnum>(statusText, true, out var status))
            status = OcrStatusEnum.PENDING;

        return new OcrJobStatusDto(
            VersionId: (Guid)row.VersionId,
            Status: status,
            JobId: (long)row.JobId,
            RequestedAt: (DateTime)row.RequestedAt,
            StartedAt: (DateTime?)row.StartedAt,
            FinishedAt: (DateTime?)row.FinishedAt,
            ErrorMessage: (string?)row.ErrorMessage,
            InvalidateDigitalSignatures: (bool)row.InvalidateDigitalSignatures
        );
    }

    public async Task<bool> HasCompletedAsync(Guid tenantId, Guid documentVersionId, CancellationToken ct)
    {
        const string sql = @"
SELECT EXISTS (
    SELECT 1
    FROM ged.ocr_job
    WHERE tenant_id = @tenantId
      AND document_version_id = @documentVersionId
      AND status = 'COMPLETED'::ged.ocr_status_enum
);";

        await using var conn = await _db.OpenAsync(ct);

        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { tenantId, documentVersionId },
                cancellationToken: ct));
    }
}