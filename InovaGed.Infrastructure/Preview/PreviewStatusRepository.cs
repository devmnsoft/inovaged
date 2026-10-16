using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Preview;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Preview;

public sealed class PreviewStatusRepository : IPreviewStatusRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<PreviewStatusRepository> _logger;

    public PreviewStatusRepository(IDbConnectionFactory db, ILogger<PreviewStatusRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HasCanonicalStatusTypeAsync(CancellationToken ct)
    {
        const string sql = """
SELECT c.udt_schema = 'ged'
   AND c.udt_name = 'preview_processing_status'
   AND (
        SELECT count(DISTINCT e.enumlabel)
        FROM pg_enum e
        JOIN pg_type t ON t.oid = e.enumtypid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged'
          AND t.typname = 'preview_processing_status'
          AND e.enumlabel IN ('PENDING', 'PROCESSING', 'READY', 'FAILED', 'CANCELED', 'ERROR')
   ) = 6
FROM information_schema.columns c
WHERE c.table_schema = 'ged'
  AND c.table_name = 'preview_status'
  AND c.column_name = 'status';
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<PreviewStatusDto?> GetAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var columns = await conn.QueryAsync<ColumnMetadataRow>(new CommandDefinition(@"
SELECT c.column_name AS ""ColumnName"", true AS ""Exists""
FROM information_schema.columns c
WHERE c.table_schema = 'ged'
  AND c.table_name = 'preview_status'
  AND c.column_name = ANY(@columns);", new
            {
                columns = new[] { "preview_path", "error_message", "error", "last_error", "error_text", "preview_error", "message", "preview_attempts", "attempts", "updated_at", "finished_at", "requested_at", "requested_at_kind", "preview_generated_at", "last_attempt_at", "attempt_started_at", "current_attempt_id", "persistence_error", "pending_preview_path", "outcome_confirmed_at", "cancel_requested" }
            }, cancellationToken: ct));

            var map = columns.ToDictionary(x => x.ColumnName, x => x.Exists, StringComparer.OrdinalIgnoreCase);

            var errorColumn = new[] { "error_message", "error", "last_error", "error_text", "preview_error", "message" }.FirstOrDefault(c => map.ContainsKey(c));
            var previewPathColumn = map.ContainsKey("preview_path") ? "preview_path" : null;
            var attemptsColumn = new[] { "attempts", "preview_attempts" }.FirstOrDefault(c => map.ContainsKey(c));
            var lastUpdatedColumn = new[] { "finished_at", "updated_at", "last_attempt_at", "preview_generated_at", "requested_at" }.FirstOrDefault(c => map.ContainsKey(c));
            var lastAttemptColumn = map.ContainsKey("last_attempt_at") ? "last_attempt_at" : null;
            var requestedAtColumn = map.ContainsKey("requested_at") ? "requested_at" : null;
            var requestedAtKindColumn = map.ContainsKey("requested_at_kind") ? "requested_at_kind" : null;
            var finishedAtColumn = map.ContainsKey("finished_at") ? "finished_at" : null;
            var attemptStartedColumn = map.ContainsKey("attempt_started_at") ? "attempt_started_at" : null;
            var attemptIdColumn = map.ContainsKey("current_attempt_id") ? "current_attempt_id" : null;
            var persistenceColumn = map.ContainsKey("persistence_error") ? "persistence_error" : null;
            var pendingPathColumn = map.ContainsKey("pending_preview_path") ? "pending_preview_path" : null;
            var confirmedColumn = map.ContainsKey("outcome_confirmed_at") ? "outcome_confirmed_at" : null;
            var cancelColumn = map.ContainsKey("cancel_requested") ? "cancel_requested" : null;

            var sql = $@"
SELECT
    tenant_id AS ""TenantId"",
    document_version_id AS ""VersionId"",
    status::text AS ""Status"",
    {(previewPathColumn is null ? "NULL::text" : previewPathColumn)} AS ""PreviewPath"",
    {(errorColumn is null ? "NULL::text" : errorColumn)} AS ""ErrorMessage"",
    {(attemptsColumn is null ? "0" : attemptsColumn)}::int AS ""Attempts"",
    {(lastUpdatedColumn is null ? "NULL::timestamptz" : lastUpdatedColumn)} AS ""LastUpdatedAt"",
    {(requestedAtColumn is null ? "NULL::timestamptz" : requestedAtColumn)} AS ""RequestedAt"",
    {(requestedAtKindColumn is null ? "'UNSPECIFIED'::text" : requestedAtKindColumn)} AS ""RequestedAtKind"",
    {(finishedAtColumn is null ? "NULL::timestamptz" : finishedAtColumn)} AS ""FinishedAt"",
    {(lastAttemptColumn is null ? "NULL::timestamptz" : lastAttemptColumn)} AS ""LastAttemptAt"",
    {(attemptStartedColumn is null ? "NULL::timestamptz" : attemptStartedColumn)} AS ""AttemptStartedAt"",
    {(confirmedColumn is null ? "NULL::timestamptz" : confirmedColumn)} AS ""OutcomeConfirmedAt"",
    {(attemptIdColumn is null ? "NULL::uuid" : attemptIdColumn)} AS ""CurrentAttemptId"",
    {(persistenceColumn is null ? "NULL::text" : persistenceColumn)} AS ""PersistenceError"",
    {(pendingPathColumn is null ? "NULL::text" : pendingPathColumn)} AS ""PendingPreviewPath"",
    {(cancelColumn is null ? "false" : cancelColumn)} AS ""CancelRequested""
FROM ged.preview_status
WHERE tenant_id = @tenantId AND document_version_id = @versionId
LIMIT 1;";

            var row = await conn.QuerySingleOrDefaultAsync<PreviewStatusRow>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct));
            if (row is null) return null;

            return new PreviewStatusDto
            {
                TenantId = row.TenantId,
                VersionId = row.VersionId,
                Status = ParseStatus(row.Status),
                PreviewPath = row.PreviewPath,
                ErrorMessage = row.ErrorMessage,
                Attempts = row.Attempts,
                LastUpdatedAt = row.LastUpdatedAt ?? row.FinishedAt ?? row.LastAttemptAt ?? row.RequestedAt,
                RequestedAt = row.RequestedAt,
                RequestedAtKind = string.IsNullOrWhiteSpace(row.RequestedAtKind) ? "UNSPECIFIED" : row.RequestedAtKind,
                FinishedAt = row.FinishedAt,
                LastAttemptAt = row.LastAttemptAt,
                AttemptStartedAt = row.AttemptStartedAt,
                OutcomeConfirmedAt = row.OutcomeConfirmedAt,
                CurrentAttemptId = row.CurrentAttemptId,
                PersistenceError = row.PersistenceError,
                PendingPreviewPath = row.PendingPreviewPath,
                CancelRequested = row.CancelRequested
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar preview status. Tenant={TenantId} Version={VersionId}", tenantId, versionId);
            throw;
        }
    }

    public Task UpsertAsync(Guid tenantId, Guid versionId, PreviewProcessingStatus status, string? previewPath, string? errorMessage, DateTimeOffset? requestedAt, DateTimeOffset? finishedAt, CancellationToken ct)
        => UpsertAsync(new PreviewStatusUpsert
        {
            TenantId = tenantId,
            VersionId = versionId,
            Status = status,
            PreviewPath = previewPath,
            ErrorMessage = errorMessage,
            RequestedAt = requestedAt,
            FinishedAt = finishedAt
        }, ct);

    public async Task UpsertAsync(PreviewStatusUpsert request, CancellationToken ct)
    {
        try
        {
            var current = await GetAsync(request.TenantId, request.VersionId, ct);
            if (!PreviewStatePolicy.CanTransition(current?.Status, request.Status, out var impediment))
            {
                _logger.LogWarning("Transição de preview recusada. Tenant={TenantId} Version={VersionId} From={From} To={To}", request.TenantId, request.VersionId, current?.Status, request.Status);
                throw new InvalidOperationException(impediment);
            }

            var observedAt = DateTimeOffset.UtcNow;
            var suppliedRequest = request.RequestedAt.HasValue && request.RequestedAt.Value != default;
            var effectiveRequestedAt = suppliedRequest ? request.RequestedAt!.Value : observedAt;
            var requestedAtKind = NormalizeRequestedAtKind(request.RequestedAtKind, suppliedRequest);
            var status = request.Status;
            var terminalStatus = status is PreviewProcessingStatus.Ready or PreviewProcessingStatus.Error or PreviewProcessingStatus.Canceled;
            DateTimeOffset? effectiveFinishedAt = terminalStatus ? request.FinishedAt ?? observedAt : null;
            var effectivePreviewPath = status == PreviewProcessingStatus.Ready ? request.PreviewPath : null;
            var effectiveErrorMessage = status == PreviewProcessingStatus.Ready ? null : request.ErrorMessage;
            var attemptId = request.AttemptId is Guid id && id != Guid.Empty ? id : (Guid?)null;
            const string sql = """
                INSERT INTO ged.preview_status (
                    tenant_id, document_version_id, status, preview_path, error_message,
                    requested_at, requested_at_kind, finished_at, attempts, last_attempt_at,
                    attempt_started_at, current_attempt_id, persistence_error, pending_preview_path, outcome_confirmed_at)
                VALUES (
                    @tenantId, @versionId, CAST(@statusText AS ged.preview_processing_status), @previewPath, @errorMessage,
                    @requestedAt, @requestedAtKind, @finishedAt,
                    CASE WHEN CAST(@statusText AS text) = 'PROCESSING' AND @attemptId IS NOT NULL THEN 1 ELSE 0 END,
                    CASE WHEN CAST(@statusText AS text) = 'PROCESSING' AND @attemptId IS NOT NULL THEN now() ELSE NULL END,
                    CASE WHEN CAST(@statusText AS text) = 'PROCESSING' AND @attemptId IS NOT NULL THEN now() ELSE NULL END,
                    CASE WHEN CAST(@statusText AS text) = 'PROCESSING' THEN @attemptId ELSE NULL END,
                    @persistenceError, @pendingPreviewPath,
                    CASE WHEN @confirmOutcome THEN now() ELSE NULL END)
                ON CONFLICT (tenant_id, document_version_id)
                DO UPDATE SET status = EXCLUDED.status,
                              preview_path = CASE
                                  WHEN EXCLUDED.status = 'READY'::ged.preview_processing_status THEN EXCLUDED.preview_path
                                  ELSE NULL
                              END,
                              error_message = EXCLUDED.error_message,
                              requested_at = COALESCE(ged.preview_status.requested_at, EXCLUDED.requested_at, now()),
                              requested_at_kind = CASE
                                  WHEN ged.preview_status.requested_at IS NOT NULL THEN ged.preview_status.requested_at_kind
                                  ELSE EXCLUDED.requested_at_kind
                              END,
                              finished_at = CASE
                                  WHEN EXCLUDED.status IN ('READY'::ged.preview_processing_status, 'FAILED'::ged.preview_processing_status, 'CANCELED'::ged.preview_processing_status) THEN COALESCE(EXCLUDED.finished_at, now())
                                  ELSE NULL
                              END,
                              attempts = COALESCE(ged.preview_status.attempts, 0) + CASE
                                  WHEN EXCLUDED.status = 'PROCESSING'::ged.preview_processing_status
                                   AND EXCLUDED.current_attempt_id IS NOT NULL
                                   AND ged.preview_status.current_attempt_id IS DISTINCT FROM EXCLUDED.current_attempt_id THEN 1
                                  ELSE 0
                              END,
                              current_attempt_id = CASE
                                  WHEN EXCLUDED.status = 'PROCESSING'::ged.preview_processing_status AND EXCLUDED.current_attempt_id IS NOT NULL THEN EXCLUDED.current_attempt_id
                                  ELSE ged.preview_status.current_attempt_id
                              END,
                              attempt_started_at = CASE
                                  WHEN EXCLUDED.status = 'PROCESSING'::ged.preview_processing_status
                                   AND EXCLUDED.current_attempt_id IS NOT NULL
                                   AND ged.preview_status.current_attempt_id IS DISTINCT FROM EXCLUDED.current_attempt_id THEN now()
                                  ELSE ged.preview_status.attempt_started_at
                              END,
                              last_attempt_at = CASE
                                  WHEN EXCLUDED.status = 'PROCESSING'::ged.preview_processing_status
                                   AND EXCLUDED.current_attempt_id IS NOT NULL
                                   AND ged.preview_status.current_attempt_id IS DISTINCT FROM EXCLUDED.current_attempt_id THEN now()
                                  ELSE ged.preview_status.last_attempt_at
                              END,
                              persistence_error = EXCLUDED.persistence_error,
                              pending_preview_path = EXCLUDED.pending_preview_path,
                              outcome_confirmed_at = CASE
                                  WHEN EXCLUDED.status = 'READY'::ged.preview_processing_status THEN COALESCE(ged.preview_status.outcome_confirmed_at, now())
                                  WHEN EXCLUDED.status = 'PROCESSING'::ged.preview_processing_status
                                   AND EXCLUDED.current_attempt_id IS NOT NULL
                                   AND ged.preview_status.current_attempt_id IS DISTINCT FROM EXCLUDED.current_attempt_id THEN NULL
                                  ELSE ged.preview_status.outcome_confirmed_at
                              END
                """;
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                tenantId = request.TenantId,
                versionId = request.VersionId,
                statusText = ToDatabaseStatus(status),
                previewPath = effectivePreviewPath,
                errorMessage = effectiveErrorMessage,
                requestedAt = effectiveRequestedAt,
                requestedAtKind,
                finishedAt = effectiveFinishedAt,
                attemptId,
                persistenceError = status == PreviewProcessingStatus.Ready ? null : request.PersistenceError,
                pendingPreviewPath = status == PreviewProcessingStatus.Ready ? null : request.PendingPreviewPath,
                confirmOutcome = request.ConfirmOutcome && status == PreviewProcessingStatus.Ready
            }, cancellationToken: ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha em upsert de preview status. Tenant={TenantId} Version={VersionId}", request.TenantId, request.VersionId);
            throw;
        }
    }

    private static PreviewProcessingStatus ParseStatus(string? status) =>
        status?.Trim().ToUpperInvariant() switch
        {
            "READY" => PreviewProcessingStatus.Ready,
            "PROCESSING" => PreviewProcessingStatus.Processing,
            "FAILED" or "ERROR" => PreviewProcessingStatus.Error,
            "CANCELED" or "CANCELLED" => PreviewProcessingStatus.Canceled,
            _ => PreviewProcessingStatus.Pending
        };

    private static string NormalizeRequestedAtKind(string? requestedKind, bool suppliedRequest)
    {
        var kind = requestedKind?.Trim().ToUpperInvariant();
        if (kind is "ORIGINAL" or "RECONSTRUCTED" or "FIRST_OBSERVATION" or "UNSPECIFIED")
            return kind;
        return suppliedRequest ? "ORIGINAL" : "FIRST_OBSERVATION";
    }

    private static string ToDatabaseStatus(PreviewProcessingStatus status) => status switch
    {
        PreviewProcessingStatus.Ready => "READY",
        PreviewProcessingStatus.Processing => "PROCESSING",
        PreviewProcessingStatus.Error => "FAILED",
        PreviewProcessingStatus.Canceled => "CANCELED",
        _ => "PENDING"
    };

    private sealed class ColumnMetadataRow
    {
        public string ColumnName { get; set; } = string.Empty;
        public bool Exists { get; set; }
    }

    private sealed class PreviewStatusRow
    {
        public Guid TenantId { get; set; }
        public Guid VersionId { get; set; }
        public string? Status { get; set; }
        public string? PreviewPath { get; set; }
        public string? ErrorMessage { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public DateTimeOffset? RequestedAt { get; set; }
        public string? RequestedAtKind { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public DateTimeOffset? LastAttemptAt { get; set; }
        public DateTimeOffset? AttemptStartedAt { get; set; }
        public DateTimeOffset? OutcomeConfirmedAt { get; set; }
        public Guid? CurrentAttemptId { get; set; }
        public string? PersistenceError { get; set; }
        public string? PendingPreviewPath { get; set; }
        public bool CancelRequested { get; set; }
    }
}
