using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ocr;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;
using Npgsql;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrJobRepository : IOcrJobRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OcrJobRepository> _logger;

    private readonly OcrOptions _options;
    // Scoped repository: every claim gets an owner that another worker cannot reuse.
    private readonly string _owner = Guid.NewGuid().ToString("N");

    public OcrJobRepository(IDbConnectionFactory db, ILogger<OcrJobRepository> logger, IOptions<OcrOptions>? options = null)
    {
        _db = db;
        _logger = logger;
        _options = options?.Value ?? new OcrOptions();
    }

    public async Task<long> EnqueueAsync(
        Guid tenantId,
        Guid documentVersionId,
        Guid? requestedBy,
        bool invalidateDigitalSignatures,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new { key = $"ocr:{tenantId:N}:{documentVersionId:N}" }, tx, cancellationToken: ct));
        var preserveCompleted = !invalidateDigitalSignatures && await HasSearchableTextAsync(conn, tenantId, documentVersionId, tx, ct);
        {

            const string existingSql = @"
SELECT id
FROM ged.ocr_job
WHERE tenant_id = @tenantId
  AND document_version_id = @documentVersionId
  AND (status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
       OR (status = 'COMPLETED'::ged.ocr_status_enum AND @preserveCompleted))
ORDER BY requested_at DESC
LIMIT 1;";

            var existingId = await conn.ExecuteScalarAsync<long?>(
                new CommandDefinition(
                    existingSql,
                    new { tenantId, documentVersionId, preserveCompleted },
                    transaction: tx, cancellationToken: ct));

            if (existingId.HasValue)
            {
                _logger.LogInformation(
                    "OCR não enfileirado porque já existe job ativo/concluído. JobId={JobId}, Tenant={TenantId}, Version={VersionId}",
                    existingId.Value,
                    tenantId,
                    documentVersionId);

                await tx.CommitAsync(ct);
                return existingId.Value;
            }
        }

        const string sql = @"
INSERT INTO ged.ocr_job
  (tenant_id, document_version_id, status, requested_by, invalidate_digital_signatures)
VALUES
  (@tenantId, @documentVersionId, 'PENDING'::ged.ocr_status_enum, @requestedBy, @invalidate)
RETURNING id;";
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql,
            new { tenantId, documentVersionId, requestedBy, invalidate = invalidateDigitalSignatures },
            transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return id;
    }

    public Task<OcrJobDto?> DequeueAndMarkProcessingAsync(CancellationToken ct) =>
        AcquireAsync(TimeSpan.FromSeconds(Math.Max(180, _options.LeaseSeconds)), ct);

    private async Task<OcrJobDto?> AcquireAsync(TimeSpan leaseTime, CancellationToken ct)
    {
        const string sql = @"
WITH lock_gate AS (
  SELECT pg_try_advisory_xact_lock(91827364) AS ok
),
capacity AS (
  SELECT count(*)::int AS running
  FROM ged.ocr_job
  WHERE status = 'PROCESSING'::ged.ocr_status_enum
    AND lease_expires_at IS NOT NULL
    AND lease_expires_at >= now()
),
cte AS (
  SELECT id
  FROM ged.ocr_job
  CROSS JOIN lock_gate
  CROSS JOIN capacity
  WHERE
    lock_gate.ok = true
    AND capacity.running < @maxConcurrent
    AND coalesce(attempts, 0) < @maxAttempts
    AND NOT coalesce(cancel_requested, false)
    AND (next_attempt_at IS NULL OR next_attempt_at <= now())
    AND (
    status = 'PENDING'::ged.ocr_status_enum
    OR (
        status = 'PROCESSING'::ged.ocr_status_enum
        AND (lease_expires_at IS NULL OR lease_expires_at < now())
    )
    )
  ORDER BY requested_at
  FOR UPDATE SKIP LOCKED
  LIMIT 1
)
UPDATE ged.ocr_job j
SET status = 'PROCESSING'::ged.ocr_status_enum,
    started_at = now(),
    attempts = coalesce(j.attempts, 0) + 1,
    last_attempt_at = now(),
    locked_by = @owner,
    locked_at = now(),
    updated_at = now(),
    finished_at = null,
    lease_expires_at = LEAST(now() + (@leaseSeconds || ' seconds')::interval, now() + (@maxJobMinutes || ' minutes')::interval),
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

        // Acquire before reading capacity so a fresh statement snapshot sees committed claims.
        if (!await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT pg_try_advisory_xact_lock(91827364);", transaction: tx, cancellationToken: ct)))
            return null;

        // Bound recovery even when a process dies on every attempt. ERROR is the dead-letter queue.
        await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE ged.ocr_job
SET status = 'CANCELLED'::ged.ocr_status_enum, finished_at = now(), updated_at = now(),
    lease_expires_at = null, locked_by = null, locked_at = null,
    error_message = 'Processamento cancelado por solicitação.'
WHERE status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
  AND coalesce(cancel_requested, false);

UPDATE ged.ocr_job
SET status = 'ERROR'::ged.ocr_status_enum, finished_at = now(), updated_at = now(),
    lease_expires_at = null, locked_by = null, locked_at = null,
    failure_code = 'OCR_ATTEMPTS_EXHAUSTED',
    error_message = 'Limite de tentativas atingido após interrupção. Reprocessamento manual necessário.'
WHERE status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
  AND coalesce(attempts, 0) >= @maxAttempts
  AND (status = 'PENDING'::ged.ocr_status_enum OR lease_expires_at IS NULL OR lease_expires_at < now());",
            new { maxAttempts = Math.Max(1, _options.MaxAttempts) }, tx, cancellationToken: ct));

        var job = await conn.QuerySingleOrDefaultAsync<OcrJobDto>(
            new CommandDefinition(
                sql,
                new
                {
                    leaseSeconds = (int)Math.Clamp(leaseTime.TotalSeconds, 180, 86400),
                    maxConcurrent = Math.Max(1, _options.MaxParallelJobs),
                    maxAttempts = Math.Max(1, _options.MaxAttempts),
                    maxJobMinutes = Math.Max(1, _options.MaxJobMinutes),
                    owner = _owner
                },
                transaction: tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);

        return job;
    }

    public async Task<int> GetAttemptsAsync(long jobId, CancellationToken ct)
    {
        const string sql = "select coalesce(attempts, 0) from ged.ocr_job where id = @jobId;";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "42703")
        {
            _logger.LogWarning(ex, "Coluna ged.ocr_job.attempts ausente. A tentativa será contada como a primeira. JobId={JobId}", jobId);
            return 0;
        }
    }

    public async Task RenewLeaseAsync(long jobId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET lease_expires_at = LEAST(now() + (@leaseSeconds || ' seconds')::interval,
        started_at + (@maxJobMinutes || ' minutes')::interval), updated_at = now()
WHERE id = @jobId AND locked_by = @owner
  AND lease_expires_at > now()
  AND started_at + (@maxJobMinutes || ' minutes')::interval > now()
  AND NOT coalesce(cancel_requested, false)
  AND status = 'PROCESSING'::ged.ocr_status_enum;";

        await using var conn = await _db.OpenAsync(ct);

        var renewed = await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    jobId,
                    leaseSeconds = Math.Max(180, _options.LeaseSeconds),
                    maxJobMinutes = Math.Max(1, _options.MaxJobMinutes),
                    owner = _owner
                },
                cancellationToken: ct));
        if (renewed != 1) throw new InvalidOperationException("OCR lease expirado, cancelado ou pertencente a outro worker.");
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
WHERE id = @jobId AND locked_by = @owner
  AND status = 'PROCESSING'::ged.ocr_status_enum AND lease_expires_at > now()
  AND NOT coalesce(cancel_requested, false);";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { jobId, documentId, resultVersionId, owner = _owner },
                cancellationToken: ct));
    }

    public async Task MarkErrorAsync(long jobId, string errorMessage, CancellationToken ct)
    {
        await MarkErrorAsync(jobId, errorMessage, null, ct);
    }

    public async Task MarkErrorAsync(long jobId, string errorMessage, string? errorDetailsJson, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'ERROR'::ged.ocr_status_enum,
    error_message = @error,
    error_details_json = CASE WHEN @details IS NULL THEN error_details_json ELSE CAST(@details AS jsonb) END,
    failure_code = COALESCE(@failureCode, failure_code),
    finished_at = now(),
    updated_at = now(),
    lease_expires_at = null,
    locked_by = null,
    locked_at = null
WHERE id = @jobId AND locked_by = @owner
  AND status = 'PROCESSING'::ged.ocr_status_enum AND lease_expires_at > now()
  AND NOT coalesce(cancel_requested, false);";

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        jobId,
                        owner = _owner,
                        error = errorMessage,
                        details = errorDetailsJson,
                        failureCode = ExtractFailureCode(errorDetailsJson)
                    },
                    cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "42703")
        {
            if (ex.MessageText.Contains("error_details_json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex,
                    "Coluna ged.ocr_job.error_details_json ausente. Aplicando fallback sem detalhes JSON. JobId={JobId}",
                    jobId);
            }
            else
            {
                _logger.LogWarning(ex,
                    "Schema ged.ocr_job desatualizado. Aplicando fallback legacy para marcar erro OCR. JobId={JobId}",
                    jobId);
            }

            await MarkErrorLegacyAsync(jobId, errorMessage, ct);
        }
    }

    private async Task MarkErrorLegacyAsync(long jobId, string errorMessage, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'ERROR'::ged.ocr_status_enum,
    error_message = @errorMessage,
    finished_at = now()
WHERE id = @jobId AND locked_by = @owner
  AND status = 'PROCESSING'::ged.ocr_status_enum AND lease_expires_at > now()
  AND NOT coalesce(cancel_requested, false);";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { jobId, errorMessage, owner = _owner },
                cancellationToken: ct));
    }

    private static string? ExtractFailureCode(string? errorDetailsJson)
    {
        if (string.IsNullOrWhiteSpace(errorDetailsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(errorDetailsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in new[] { "code", "Code", "failureCode", "failure_code" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    
    public async Task MarkRetryAsync(long jobId, int attempts, DateTimeOffset nextAttemptAt, string errorMessage, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'PENDING'::ged.ocr_status_enum,
    attempts = GREATEST(attempts, @attempts),
    last_attempt_at = now(),
    next_attempt_at = @nextAttemptAt,
    lease_expires_at = null,
    error_message = @error
WHERE id = @jobId AND locked_by = @owner
  AND status = 'PROCESSING'::ged.ocr_status_enum AND lease_expires_at > now()
  AND NOT coalesce(cancel_requested, false);";

        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { jobId, attempts, nextAttemptAt, error = errorMessage, owner = _owner }, cancellationToken: ct));
    }

    public async Task<bool> IsCancelRequestedAsync(long jobId, CancellationToken ct)
    {
        const string sql = @"SELECT coalesce(cancel_requested,false) OR status='CANCELLED'::ged.ocr_status_enum FROM ged.ocr_job WHERE id=@jobId;";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
    }

    public async Task<OcrJobLease?> LeaseNextAsync(TimeSpan leaseTime, CancellationToken ct)
    {
        var job = await AcquireAsync(leaseTime, ct);
        return job is null ? null : new OcrJobLease(job.Id, job.TenantId,
            job.DocumentVersionId, job.InvalidateDigitalSignatures);
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

        var completed = await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { tenantId, documentVersionId },
                cancellationToken: ct));
        return completed && await HasSearchableTextAsync(conn, tenantId, documentVersionId, null, ct);
    }

    private static async Task<bool> HasSearchableTextAsync(NpgsqlConnection conn, Guid tenantId, Guid documentVersionId, NpgsqlTransaction? tx, CancellationToken ct)
    {
        var columns = (await conn.QueryAsync<string>(new CommandDefinition(@"
SELECT column_name FROM information_schema.columns WHERE table_schema='ged'
  AND table_name='document_search' AND column_name IN ('version_id','document_version_id');",
            transaction: tx, cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);
        var predicates = new List<string>();
        if (columns.Contains("version_id")) predicates.Add("ds.version_id IN (j.document_version_id,j.result_version_id)");
        if (columns.Contains("document_version_id")) predicates.Add("ds.document_version_id IN (j.document_version_id,j.result_version_id)");
        if (predicates.Count == 0) return false;
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition($@"
SELECT EXISTS (
  SELECT 1 FROM ged.document_search ds
  JOIN ged.ocr_job j ON j.tenant_id = ds.tenant_id
  WHERE j.tenant_id = @tenantId AND j.document_version_id = @documentVersionId
    AND j.status = 'COMPLETED'::ged.ocr_status_enum
    AND ({string.Join(" OR ", predicates)})
    AND NULLIF(btrim(coalesce(ds.ocr_text,'')), '') IS NOT NULL
);", new { tenantId, documentVersionId }, tx, cancellationToken: ct));
    }

    public async Task<int> CancelByVersionAsync(Guid tenantId, Guid versionId, Guid? cancelledBy, string reason, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job
SET status = 'CANCELLED'::ged.ocr_status_enum,
    finished_at = now(),
    lease_expires_at = null,
    error_message = @reason,
    cancel_requested = true,
    cancel_requested_at = COALESCE(cancel_requested_at, now()),
    cancelled_by = COALESCE(@cancelledBy, cancelled_by),
    cancel_reason = COALESCE(@reason, cancel_reason)
WHERE tenant_id = @tenantId
  AND document_version_id = @versionId
  AND status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum, 'ERROR'::ged.ocr_status_enum, 'COMPLETED'::ged.ocr_status_enum);";

        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, versionId, cancelledBy, reason }, cancellationToken: ct));
    }

    public async Task<int> CancelQueueAsync(Guid tenantId, Guid? documentId, Guid? cancelledBy, string reason, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.ocr_job j
SET status = 'CANCELLED'::ged.ocr_status_enum,
    finished_at = now(),
    lease_expires_at = null,
    error_message = @reason,
    cancel_requested = true,
    cancel_requested_at = COALESCE(cancel_requested_at, now()),
    cancelled_by = COALESCE(@cancelledBy, cancelled_by),
    cancel_reason = COALESCE(@reason, cancel_reason)
WHERE j.tenant_id = @tenantId
  AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
  AND (@documentId IS NULL OR j.document_version_id IN (
      SELECT dv.id FROM ged.document_version dv WHERE dv.tenant_id = @tenantId AND dv.document_id = @documentId
  ));";

        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, cancelledBy, reason }, cancellationToken: ct));
    }
}
