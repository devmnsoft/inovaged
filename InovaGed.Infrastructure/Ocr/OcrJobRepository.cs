using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrJobRepository : IOcrJobRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrJobRepository> _logger;

    // você pode ajustar via config também; aqui fixo em 10 min
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

        var conn = await _db.OpenAsync(ct);

        var id = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, new
            {
                tenantId,
                documentVersionId,
                requestedBy,
                invalidate = invalidateDigitalSignatures
            }, cancellationToken: ct));

        _logger.LogInformation("OCR job enfileirado. JobId={JobId}, Tenant={TenantId}, Version={VersionId}",
            id, tenantId, documentVersionId);

        return id;
    }

    public async Task<OcrJobDto?> DequeueAndMarkProcessingAsync(CancellationToken ct)
    {
        // ✅ pega PENDING ou PROCESSING expirado (lease vencido)
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
  j.invalidate_digital_signatures AS InvalidateDigitalSignatures;";

        var conn = await _db.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<OcrJobDto>(
            new CommandDefinition(sql, new
            {
                leaseSeconds = (int)LeaseDuration.TotalSeconds
            }, cancellationToken: ct));
    }

    public async Task RenewLeaseAsync(long jobId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET lease_expires_at = now() + (@leaseSeconds || ' seconds')::interval
WHERE id = @jobId
  AND status = 'PROCESSING'::ged.ocr_status_enum;";

        var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            jobId,
            leaseSeconds = (int)LeaseDuration.TotalSeconds
        }, cancellationToken: ct));
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

        var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
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

        var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId, error = errorMessage }, cancellationToken: ct));
    }
}
