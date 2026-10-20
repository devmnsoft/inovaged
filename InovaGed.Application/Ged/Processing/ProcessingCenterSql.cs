namespace InovaGed.Application.Ged.Processing;

public static class ProcessingCenterSql
{
    public const string Jobs = """
SELECT
  'JOB' AS Source,
  j.id::text AS JobKey,
  j.document_id AS DocumentId,
  j.document_version_id AS VersionId,
  coalesce(d.title, 'Documento sem título') AS DocumentTitle,
  coalesce(dv.file_name, '') AS FileName,
  j.job_type AS Lane,
  j.status AS Status,
  j.attempt_count AS Attempts,
  j.max_attempts AS MaxAttempts,
  j.error_message AS ErrorMessage,
  j.created_at AS RequestedAt,
  j.started_at AS StartedAt,
  j.finished_at AS FinishedAt
FROM ged.processing_job j
LEFT JOIN ged.document d
  ON d.tenant_id = j.tenant_id
 AND d.id = j.document_id
LEFT JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
WHERE j.tenant_id = @tenantId
  AND j.reg_status = 'A'
  AND j.created_at >= @from
  AND j.created_at < @to
  AND (@lane IS NULL OR j.job_type = @lane)
  AND (cardinality(@statuses) = 0 OR j.status = ANY(@statuses))
  AND (@name IS NULL OR d.title ILIKE @name)
  AND (@userId IS NULL OR d.created_by = @userId)
  AND (@documentTypeId IS NULL OR d.document_type_id = @documentTypeId)
ORDER BY j.created_at DESC
LIMIT @take;
""";

    public const string JobsWithoutDocumentFilters = """
SELECT
  'JOB' AS Source,
  j.id::text AS JobKey,
  j.document_id AS DocumentId,
  j.document_version_id AS VersionId,
  coalesce(d.title, 'Documento sem título') AS DocumentTitle,
  coalesce(dv.file_name, '') AS FileName,
  j.job_type AS Lane,
  j.status AS Status,
  j.attempt_count AS Attempts,
  j.max_attempts AS MaxAttempts,
  j.error_message AS ErrorMessage,
  j.created_at AS RequestedAt,
  j.started_at AS StartedAt,
  j.finished_at AS FinishedAt
FROM ged.processing_job j
LEFT JOIN ged.document d
  ON d.tenant_id = j.tenant_id
 AND d.id = j.document_id
LEFT JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
WHERE j.tenant_id = @tenantId
  AND j.reg_status = 'A'
  AND j.created_at >= @from
  AND j.created_at < @to
  AND (@lane IS NULL OR j.job_type = @lane)
  AND (cardinality(@statuses) = 0 OR j.status = ANY(@statuses))
  AND (@name IS NULL OR d.title ILIKE @name)
ORDER BY j.created_at DESC
LIMIT @take;
""";

    public const string Ocr = """
SELECT
  'OCR' AS Source,
  j.id::text AS JobKey,
  d.id AS DocumentId,
  j.document_version_id AS VersionId,
  coalesce(d.title, 'Documento sem título') AS DocumentTitle,
  coalesce(dv.file_name, '') AS FileName,
  'OCR' AS Lane,
  j.status::text AS Status,
  coalesce(j.attempts, 0) AS Attempts,
  3 AS MaxAttempts,
  j.error_message AS ErrorMessage,
  j.requested_at AS RequestedAt,
  j.started_at AS StartedAt,
  j.finished_at AS FinishedAt
FROM ged.ocr_job j
JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
JOIN ged.document d
  ON d.tenant_id = dv.tenant_id
 AND d.id = dv.document_id
WHERE j.tenant_id = @tenantId
  AND j.requested_at >= @from
  AND j.requested_at < @to
  AND (cardinality(@statuses) = 0 OR j.status::text = ANY(@statuses))
  AND (@name IS NULL OR d.title ILIKE @name)
  AND (@userId IS NULL OR d.created_by = @userId)
  AND (@documentTypeId IS NULL OR d.document_type_id = @documentTypeId)
ORDER BY j.requested_at DESC
LIMIT @take;
""";

    public const string OcrWithoutOptionalColumns = """
SELECT
  'OCR' AS Source,
  j.id::text AS JobKey,
  d.id AS DocumentId,
  j.document_version_id AS VersionId,
  coalesce(d.title, 'Documento sem título') AS DocumentTitle,
  coalesce(dv.file_name, '') AS FileName,
  'OCR' AS Lane,
  j.status::text AS Status,
  0 AS Attempts,
  3 AS MaxAttempts,
  NULL::text AS ErrorMessage,
  j.requested_at AS RequestedAt,
  j.started_at AS StartedAt,
  j.finished_at AS FinishedAt
FROM ged.ocr_job j
JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
JOIN ged.document d
  ON d.tenant_id = dv.tenant_id
 AND d.id = dv.document_id
WHERE j.tenant_id = @tenantId
  AND j.requested_at >= @from
  AND j.requested_at < @to
  AND (cardinality(@statuses) = 0 OR j.status::text = ANY(@statuses))
  AND (@name IS NULL OR d.title ILIKE @name)
ORDER BY j.requested_at DESC
LIMIT @take;
""";

    public const string Uploads = """
SELECT
  'UPLOAD' AS Source,
  s.id::text AS JobKey,
  s.document_id AS DocumentId,
  s.version_id AS VersionId,
  coalesce(s.original_file_name, 'Upload') AS DocumentTitle,
  coalesce(s.original_file_name, '') AS FileName,
  'UPLOAD' AS Lane,
  s.status AS Status,
  s.received_chunks AS Attempts,
  s.total_chunks AS MaxAttempts,
  s.error_message AS ErrorMessage,
  s.created_at AS RequestedAt,
  s.created_at AS StartedAt,
  s.completed_at AS FinishedAt
FROM ged.upload_session s
WHERE s.tenant_id = @tenantId
  AND s.reg_status = 'A'
  AND s.created_at >= @from
  AND s.created_at < @to
  AND (cardinality(@statuses) = 0 OR s.status = ANY(@statuses))
  AND (@name IS NULL OR s.original_file_name ILIKE @name)
  AND (@userId IS NULL OR s.user_id = @userId)
  AND @documentTypeId IS NULL
ORDER BY s.created_at DESC
LIMIT @take;
""";

    public const string JobCounts = """
SELECT
  count(*) FILTER (WHERE j.status = 'PENDING')::int AS PendingCount,
  count(*) FILTER (WHERE j.status = 'PROCESSING')::int AS ProcessingCount,
  count(*) FILTER (WHERE j.status = 'FAILED' AND j.created_at >= now() - interval '24 hours')::int AS Failed24h,
  count(*) FILTER (WHERE j.status = 'COMPLETED' AND j.finished_at >= now() - interval '24 hours')::int AS Completed24h,
  count(*) FILTER (WHERE j.status = 'CANCELLED' AND j.finished_at >= now() - interval '24 hours')::int AS Cancelled24h,
  count(*) FILTER (WHERE j.job_type = 'PREVIEW' AND j.status IN ('PENDING','PROCESSING'))::int AS PreviewActive,
  coalesce(avg(extract(epoch from (j.finished_at - j.started_at))) FILTER (WHERE j.status = 'COMPLETED' AND j.finished_at >= now() - interval '24 hours' AND j.started_at IS NOT NULL), 0)::int AS AverageProcessingSeconds,
  count(*) FILTER (WHERE j.attempt_count > 1 AND j.updated_at >= now() - interval '24 hours')::int AS ReprocessCount
FROM ged.processing_job j
WHERE j.tenant_id = @tenantId
  AND j.reg_status = 'A';
""";

    public const string UploadCounts = """
SELECT
  count(*) FILTER (WHERE s.status IN ('OPEN','RECEIVING','COMPLETING'))::int AS ActiveCount,
  coalesce(sum(s.total_size_bytes) FILTER (WHERE s.created_at >= now() - interval '24 hours'), 0)::bigint AS ReceivedBytes24h
FROM ged.upload_session s
WHERE s.tenant_id = @tenantId
  AND s.reg_status = 'A';
""";

    public const string RetryJob = """
UPDATE ged.processing_job AS target
SET status = 'PENDING',
    attempt_count = 0,
    next_attempt_at = now(),
    locked_by = NULL,
    locked_at = NULL,
    finished_at = NULL,
    updated_at = now(),
    error_message = left(coalesce(target.error_message, '') || ' Reprocessado pela central.', 2000)
WHERE target.tenant_id = @tenantId
  AND target.id = @jobId
  AND target.reg_status = 'A'
  AND target.status IN ('FAILED','CANCELLED')
  AND NOT EXISTS (
    SELECT 1
    FROM ged.processing_job other
    WHERE other.tenant_id = target.tenant_id
      AND other.job_type = target.job_type
      AND other.reg_status = 'A'
      AND other.status IN ('PENDING','PROCESSING')
      AND other.id <> target.id
      AND coalesce(other.document_version_id, '00000000-0000-0000-0000-000000000000'::uuid) = coalesce(target.document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
      AND coalesce(other.upload_batch_item_id, '00000000-0000-0000-0000-000000000000'::uuid) = coalesce(target.upload_batch_item_id, '00000000-0000-0000-0000-000000000000'::uuid)
  );
""";

    public const string CancelJob = """
UPDATE ged.processing_job
SET status = 'CANCELLED',
    finished_at = now(),
    locked_by = NULL,
    locked_at = NULL,
    updated_at = now(),
    error_message = 'Cancelado na central de processamento.'
WHERE tenant_id = @tenantId
  AND id = @jobId
  AND reg_status = 'A'
  AND status IN ('PENDING','PROCESSING');
""";

    public const string RetryJobs = """
UPDATE ged.processing_job AS target
SET status = 'PENDING',
    attempt_count = 0,
    next_attempt_at = now(),
    locked_by = NULL,
    locked_at = NULL,
    finished_at = NULL,
    updated_at = now(),
    error_message = left(coalesce(target.error_message, '') || ' Reprocessado pela central.', 2000)
WHERE target.tenant_id = @tenantId
  AND target.id = ANY(@jobIds)
  AND target.reg_status = 'A'
  AND target.status IN ('FAILED','CANCELLED')
  AND NOT EXISTS (
    SELECT 1
    FROM ged.processing_job other
    WHERE other.tenant_id = target.tenant_id
      AND other.job_type = target.job_type
      AND other.reg_status = 'A'
      AND other.status IN ('PENDING','PROCESSING')
      AND other.id <> target.id
      AND coalesce(other.document_version_id, '00000000-0000-0000-0000-000000000000'::uuid) = coalesce(target.document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
      AND coalesce(other.upload_batch_item_id, '00000000-0000-0000-0000-000000000000'::uuid) = coalesce(target.upload_batch_item_id, '00000000-0000-0000-0000-000000000000'::uuid)
  );
""";
}
