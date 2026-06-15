using System.Diagnostics;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Ocr;
using InovaGed.Application.Preview;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class UploadBatchService : IUploadBatchService
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentBulkUploadService _bulkUpload;
    private readonly IUploadConcurrencyLimiter _limiter;
    private readonly IGedProcessingJobRepository _processingJobs;
    private readonly IAuditWriter _audit;
    private readonly ILogger<UploadBatchService> _logger;
    private readonly DocumentUploadOptions _options;
    private readonly HashSet<string> _allowedExtensions;

    public UploadBatchService(
        IDbConnectionFactory db,
        IDocumentBulkUploadService bulkUpload,
        IUploadConcurrencyLimiter limiter,
        IGedProcessingJobRepository processingJobs,
        IAuditWriter audit,
        IOptions<DocumentUploadOptions> options,
        ILogger<UploadBatchService> logger)
    {
        _db = db;
        _bulkUpload = bulkUpload;
        _limiter = limiter;
        _processingJobs = processingJobs;
        _audit = audit;
        _logger = logger;
        _options = options.Value;
        _allowedExtensions = new HashSet<string>(_options.AllowedExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Result<Guid>> StartAsync(Guid tenantId, Guid userId, StartUploadBatchRequestDto request, CancellationToken ct)
    {
        if (!request.FolderId.HasValue || request.FolderId.Value == Guid.Empty) return Result<Guid>.Fail("VALIDATION", "Selecione uma pasta para enviar documentos.");
        if (request.TotalFiles <= 0) return Result<Guid>.Fail("VALIDATION", "Informe ao menos um arquivo para iniciar o lote.");
        if (request.TotalFiles > Math.Max(1, _options.MaxBatchFiles)) return Result<Guid>.Fail("LIMIT", $"O lote excede o limite de {_options.MaxBatchFiles} arquivos.");

        var id = Guid.NewGuid();
        var requestedFolderId = request.RequestedFolderId ?? request.FolderId;
        const string sql = """
INSERT INTO ged.upload_batch (id, tenant_id, folder_id, requested_folder_id, created_by, status, total_files, source_ip, user_agent, correlation_id)
VALUES (@id, @tenantId, @folderId, @requestedFolderId, @userId, 'OPEN', @totalFiles, @sourceIp, @userAgent, @correlationId);
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { id, tenantId, folderId = request.FolderId, requestedFolderId, userId, totalFiles = request.TotalFiles, request.SourceIp, request.UserAgent, request.CorrelationId }, cancellationToken: ct));
            _logger.LogInformation("StartBatch Tenant={TenantId} User={UserId} Batch={BatchId} RequestedFolder={RequestedFolderId} Folder={FolderId} Total={TotalFiles} CorrelationId={CorrelationId}", tenantId, userId, id, requestedFolderId, request.FolderId, request.TotalFiles, request.CorrelationId);
            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "UPLOAD_BATCH", id, "Lote de upload iniciado", null, null, new { RequestedFolderId = requestedFolderId, request.FolderId, request.TotalFiles, request.Options, request.CorrelationId }, ct);
            return Result<Guid>.Ok(id);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex, "Tabelas de upload em lote não existem. Execute a migration 2026_06_01_upload_batch.sql. Tenant={TenantId} User={UserId}", tenantId, userId);
            return Result<Guid>.Fail("UPLOAD_BATCH_SCHEMA_MISSING", "Upload em lote indisponível. Estrutura de banco pendente.");
        }
    }

    public async Task<Result<UploadBatchFileResultDto>> UploadFileAsync(Guid tenantId, Guid userId, UploadBatchFileRequestDto request, CancellationToken ct)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId;
        if (!request.FolderId.HasValue || request.FolderId.Value == Guid.Empty) return Result<UploadBatchFileResultDto>.Fail("VALIDATION", "Selecione uma pasta para enviar documentos.");
        if (request.BatchId == Guid.Empty) return Result<UploadBatchFileResultDto>.Fail("VALIDATION", "Lote inválido.");
        if (request.File is null || request.File.Length <= 0) return Result<UploadBatchFileResultDto>.Fail("VALIDATION", "Arquivo inválido.");
        if (request.File.Length > Math.Max(1, _options.MaxFileSizeMb) * 1024L * 1024L) return Result<UploadBatchFileResultDto>.Fail("LIMIT", $"O arquivo excede o limite de {_options.MaxFileSizeMb} MB.");
        var ext = Path.GetExtension(request.UploadName ?? request.File.FileName ?? string.Empty);
        var blockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".ps1", ".cmd", ".php", ".aspx", ".config", ".env", ".dll" };
        if (blockedExtensions.Contains(ext))
        {
            _logger.LogWarning("SECURITY_UPLOAD_BLOCKED Tenant={TenantId} User={UserId} Batch={BatchId} File={FileName} Extension={Extension} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, request.File.FileName, ext, correlationId);
            return Result<UploadBatchFileResultDto>.Fail("SECURITY_UPLOAD_BLOCKED", $"Extensão bloqueada por política de segurança: {ext}");
        }
        if (_allowedExtensions.Count > 0 && !_allowedExtensions.Contains(ext)) return Result<UploadBatchFileResultDto>.Fail("EXTENSION", $"Extensão não permitida: {ext}");

        await using var lease = await _limiter.AcquireAsync(tenantId, userId, request.BatchId, ct);
        if (lease is null) return Result<UploadBatchFileResultDto>.Fail("CONCURRENCY", "Há muitos uploads em andamento. Aguarde alguns segundos.");

        var itemId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        try
        {
            await EnsureBatchProcessingAsync(tenantId, request.BatchId, ct);
            await InsertItemAsync(tenantId, request, itemId, correlationId, ct);
            _logger.LogInformation("File upload started Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} Folder={FolderId} File={FileName} Size={SizeBytes} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, request.FolderId, request.File.FileName, request.File.Length, correlationId);

            if (string.Equals(request.DuplicateStrategy, "skip", StringComparison.OrdinalIgnoreCase) && request.ExistingDocumentId.HasValue)
            {
                await MarkItemSkippedAsync(tenantId, itemId, "Arquivo ignorado por regra de duplicidade.", sw.ElapsedMilliseconds, ct);
                return Result<UploadBatchFileResultDto>.Ok(new UploadBatchFileResultDto { ItemId = itemId, Status = "SKIPPED", Message = "Arquivo ignorado por duplicidade.", RequestedFolderId = request.RequestedFolderId, ResolvedFolderId = request.FolderId, CorrelationId = correlationId });
            }

            var metadata = request.Metadata;
            metadata.BatchId = request.BatchId;
            metadata.RunOcr = request.RunOcr;
            metadata.GeneratePreview = request.GeneratePreview;
            metadata.DuplicateStrategy = request.DuplicateStrategy;
            metadata.UploadName = request.UploadName;
            metadata.ExistingDocumentId = request.ExistingDocumentId;

            var result = await _bulkUpload.UploadSingleAsync(tenantId, userId, request.UserName, request.File, request.FolderId, metadata, request.IsAdmin, ct);
            if (!result.Success)
            {
                await MarkItemErrorAsync(tenantId, itemId, result.Error?.Message ?? "Falha ao persistir arquivo.", result.Error?.Code ?? "Persistência", true, sw.ElapsedMilliseconds, ct);
                return Result<UploadBatchFileResultDto>.Fail(result.Error?.Code ?? "UPLOAD", result.Error?.Message ?? "Falha ao enviar arquivo.");
            }

            var version = await GetCurrentVersionAsync(tenantId, result.Value!.DocumentId, ct);
            await UpdateItemDocumentAsync(tenantId, itemId, result.Value.DocumentId, version?.VersionId, version?.FileName, request.File.ContentType, request.File.Length, version?.ChecksumSha256, "DOCUMENT_CREATED", sw.ElapsedMilliseconds, ct);
            _logger.LogInformation("Document created Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} DocumentId={DocumentId} VersionId={VersionId} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, result.Value.DocumentId, version?.VersionId, correlationId);

            var ocrQueued = false;
            var previewQueued = false;
            if (request.GeneratePreview && version is not null)
            {
                await _processingJobs.EnqueueAsync(tenantId, result.Value.DocumentId, version.VersionId, request.BatchId, itemId, "PREVIEW", 3, ct);
                previewQueued = true;
                _logger.LogInformation("PROCESSING_JOB_CREATED Type=PREVIEW Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} VersionId={VersionId} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, version.VersionId, correlationId);
            }

            if (request.RunOcr && version?.VersionId is Guid ocrVersionId)
            {
                await _processingJobs.EnqueueAsync(tenantId, result.Value.DocumentId, ocrVersionId, request.BatchId, itemId, "OCR", 5, ct);
                ocrQueued = true;
                _logger.LogInformation("PROCESSING_JOB_CREATED Type=OCR Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} VersionId={VersionId} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, ocrVersionId, correlationId);
            }

            if (version?.VersionId is Guid indexVersionId)
            {
                await _processingJobs.EnqueueAsync(tenantId, result.Value.DocumentId, indexVersionId, request.BatchId, itemId, "SMART_INDEX", 7, ct);
            }

            await UpdateItemStatusAsync(tenantId, itemId, "QUEUED", sw.ElapsedMilliseconds, ct);
            await UpdateItemStatusAsync(tenantId, itemId, "COMPLETED", sw.ElapsedMilliseconds, ct);
            await RefreshBatchCountersAsync(tenantId, request.BatchId, finished: false, ct);
            _logger.LogInformation("File completed Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} File={FileName} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, request.File.FileName, sw.ElapsedMilliseconds, correlationId);
            return Result<UploadBatchFileResultDto>.Ok(new UploadBatchFileResultDto { ItemId = itemId, DocumentId = result.Value.DocumentId, VersionId = version?.VersionId, RequestedFolderId = request.RequestedFolderId, ResolvedFolderId = request.FolderId, Title = result.Value.Title, FileName = result.Value.FileName, UploadedAtUtc = version?.UploadedAtUtc, UploadedAtLocalFormatted = FormatUploadDate(version?.UploadedAtUtc), Status = "COMPLETED", Message = "Arquivo recebido e processamento pesado enfileirado.", OcrQueued = ocrQueued, PreviewQueued = previewQueued, CorrelationId = correlationId });
        }
        catch (OperationCanceledException)
        {
            await MarkItemErrorAsync(tenantId, itemId, "Upload cancelado/interrompido pelo cliente.", "ClientAbort", true, sw.ElapsedMilliseconds, CancellationToken.None);
            _logger.LogWarning("Client aborted Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} File={FileName} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, request.File?.FileName, correlationId);
            throw;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex, "Tabelas de upload em lote não existem durante envio de arquivo. Execute a migration 2026_06_01_upload_batch.sql. Tenant={TenantId} User={UserId} Batch={BatchId}", tenantId, userId, request.BatchId);
            return Result<UploadBatchFileResultDto>.Fail("UPLOAD_BATCH_SCHEMA_MISSING", "Upload em lote indisponível. Estrutura de banco pendente.");
        }
        catch (Exception ex)
        {
            await MarkItemErrorAsync(tenantId, itemId, "Erro interno ao enviar arquivo.", "Servidor", true, sw.ElapsedMilliseconds, CancellationToken.None);
            _logger.LogError(ex, "File failed Tenant={TenantId} User={UserId} Batch={BatchId} Item={ItemId} File={FileName} CorrelationId={CorrelationId}", tenantId, userId, request.BatchId, itemId, request.File?.FileName, correlationId);
            return Result<UploadBatchFileResultDto>.Fail("ERR", "Erro interno ao enviar arquivo.");
        }
    }

    public async Task<Result<UploadBatchStatusDto>> FinishAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        try
        {
            await RefreshBatchCountersAsync(tenantId, batchId, finished: true, ct);
            var status = await LoadStatusAsync(tenantId, batchId, includeAllItems: true, ct);
            _logger.LogInformation("Batch finished Tenant={TenantId} User={UserId} Batch={BatchId} Status={Status} Success={Success} Failed={Failed} Skipped={Skipped}", tenantId, userId, batchId, status.Status, status.Success, status.Failed, status.Skipped);
            await _audit.WriteAsync(tenantId, userId, "BATCH_EVENT", "UPLOAD_BATCH", batchId, "Lote de upload finalizado", null, null, status, ct);
            return Result<UploadBatchStatusDto>.Ok(status);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex, "Tabelas de upload em lote não existem ao finalizar lote. Execute a migration 2026_06_01_upload_batch.sql. Tenant={TenantId} User={UserId} Batch={BatchId}", tenantId, userId, batchId);
            return Result<UploadBatchStatusDto>.Fail("UPLOAD_BATCH_SCHEMA_MISSING", "Upload em lote indisponível. Estrutura de banco pendente.");
        }
    }

    public async Task<Result<UploadBatchStatusDto>> GetStatusAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        try
        {
            return Result<UploadBatchStatusDto>.Ok(await LoadStatusAsync(tenantId, batchId, includeAllItems: true, ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex, "Tabelas de upload em lote não existem ao consultar lote. Execute a migration 2026_06_01_upload_batch.sql. Tenant={TenantId} User={UserId} Batch={BatchId}", tenantId, userId, batchId);
            return Result<UploadBatchStatusDto>.Fail("UPLOAD_BATCH_SCHEMA_MISSING", "Upload em lote indisponível. Estrutura de banco pendente.");
        }
    }

    public async Task<Result<UploadBatchStatusDto>> RetryFailedAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct)
    {
        const string sql = """
UPDATE ged.upload_batch_item
SET status='PENDING', error_message=NULL, error_step=NULL, attempt=attempt+1, started_at=NULL, finished_at=NULL, elapsed_ms=NULL
WHERE tenant_id=@tenantId AND batch_id=@batchId AND status='ERROR' AND can_retry=true AND reg_status='A';
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, batchId }, cancellationToken: ct));
            return Result<UploadBatchStatusDto>.Ok(await LoadStatusAsync(tenantId, batchId, includeAllItems: true, ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogError(ex, "Tabelas de upload em lote não existem ao preparar retentativa. Execute a migration 2026_06_01_upload_batch.sql. Tenant={TenantId} User={UserId} Batch={BatchId}", tenantId, userId, batchId);
            return Result<UploadBatchStatusDto>.Fail("UPLOAD_BATCH_SCHEMA_MISSING", "Upload em lote indisponível. Estrutura de banco pendente.");
        }
    }

    public async Task<int> MarkStaleReceivingItemsAsErrorAsync(TimeSpan staleAfter, CancellationToken ct)
    {
        const string sql = """
UPDATE ged.upload_batch_item
SET status='ERROR', error_message='Upload interrompido antes da conclusão.', error_step='StaleReceiving', can_retry=true, finished_at=now(), elapsed_ms=extract(epoch from (now()-started_at))*1000
WHERE status='RECEIVING' AND started_at < now() - (@staleAfterSeconds || ' seconds')::interval AND reg_status='A';
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql, new { staleAfterSeconds = (int)staleAfter.TotalSeconds }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex, "Tabelas de upload em lote não existem ao limpar itens antigos. Execute a migration 2026_06_01_upload_batch.sql.");
            return 0;
        }
    }

    public async Task<UploadMonitorDto> GetMonitorAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
        var batches = (await conn.QueryAsync<UploadMonitorBatchDto>(new CommandDefinition("""
SELECT b.id, b.folder_id AS FolderId, b.created_by AS CreatedBy, b.created_at AS CreatedAt, b.finished_at AS FinishedAt,
       b.status, b.total_files AS TotalFiles, b.success_files AS SuccessFiles, b.failed_files AS FailedFiles,
       b.skipped_files AS SkippedFiles, b.source_ip AS SourceIp, b.user_agent AS UserAgent, b.correlation_id AS CorrelationId,
       avg(i.elapsed_ms)::float AS AvgElapsedMs
FROM ged.upload_batch b
LEFT JOIN ged.upload_batch_item i ON i.tenant_id=b.tenant_id AND i.batch_id=b.id AND i.reg_status='A'
WHERE b.tenant_id=@tenantId AND b.reg_status='A' AND b.created_at > now() - interval '7 days'
GROUP BY b.id
ORDER BY b.created_at DESC
LIMIT 100;
""", new { tenantId }, cancellationToken: ct))).AsList();
        var stale = (await conn.QueryAsync<UploadBatchItemStatusDto>(new CommandDefinition("""
SELECT id, original_file_name AS OriginalFileName, stored_file_name AS StoredFileName, document_id AS DocumentId, version_id AS VersionId, status, error_message AS ErrorMessage, error_step AS ErrorStep, can_retry AS CanRetry, size_bytes AS SizeBytes, correlation_id AS CorrelationId
FROM ged.upload_batch_item
WHERE tenant_id=@tenantId AND status='RECEIVING' AND started_at < now() - interval '10 minutes' AND reg_status='A'
ORDER BY started_at
LIMIT 100;
""", new { tenantId }, cancellationToken: ct))).AsList();
            var pendingOcr = await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*) FROM ged.ocr_job WHERE tenant_id=@tenantId AND status='PENDING'::ged.ocr_status_enum;", new { tenantId }, cancellationToken: ct));
            return new UploadMonitorDto { Batches = batches, StaleReceivingItems = stale, PendingOcrCount = pendingOcr };
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex, "Tabelas de upload em lote não existem ao carregar monitor. Execute a migration 2026_06_01_upload_batch.sql. Tenant={TenantId}", tenantId);
            return new UploadMonitorDto();
        }
    }

    private async Task InsertItemAsync(Guid tenantId, UploadBatchFileRequestDto request, Guid itemId, string correlationId, CancellationToken ct)
    {
        const string sql = """
INSERT INTO ged.upload_batch_item (id, tenant_id, batch_id, folder_id, requested_folder_id, original_file_name, content_type, size_bytes, status, started_at, attempt, correlation_id)
VALUES (@itemId, @tenantId, @batchId, @folderId, @requestedFolderId, @fileName, @contentType, @sizeBytes, 'RECEIVING', now(), 1, @correlationId);
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { itemId, tenantId, batchId = request.BatchId, request.FolderId, requestedFolderId = request.RequestedFolderId ?? request.FolderId, fileName = Path.GetFileName(request.UploadName ?? request.File.FileName), contentType = request.File.ContentType, sizeBytes = request.File.Length, correlationId }, cancellationToken: ct));
    }

    private static string? FormatUploadDate(DateTime? uploadedAtUtc)
        => uploadedAtUtc?.ToUniversalTime().ToString("dd/MM/yyyy HH:mm");

    private async Task EnsureBatchProcessingAsync(Guid tenantId, Guid batchId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("UPDATE ged.upload_batch SET status='PROCESSING', started_at=COALESCE(started_at, now()) WHERE tenant_id=@tenantId AND id=@batchId AND status='OPEN';", new { tenantId, batchId }, cancellationToken: ct));
    }

    private async Task<VersionInfo?> GetCurrentVersionAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
SELECT v.id AS VersionId, v.file_name AS FileName, v.storage_path AS StoragePath, v.checksum_sha256 AS ChecksumSha256, COALESCE(v.uploaded_at_utc, v.created_at) AS UploadedAtUtc
FROM ged.document d
JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=d.current_version_id
WHERE d.tenant_id=@tenantId AND d.id=@documentId;
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<VersionInfo>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    private sealed class VersionInfo { public Guid VersionId { get; set; } public string FileName { get; set; } = string.Empty; public string StoragePath { get; set; } = string.Empty; public string? ChecksumSha256 { get; set; } public DateTime? UploadedAtUtc { get; set; } }

    private async Task UpdateItemDocumentAsync(Guid tenantId, Guid itemId, Guid documentId, Guid? versionId, string? storedFileName, string? contentType, long sizeBytes, string? checksum, string status, long elapsedMs, CancellationToken ct)
    {
        const string sql = """
UPDATE ged.upload_batch_item
SET document_id=@documentId, version_id=@versionId, stored_file_name=@storedFileName, content_type=@contentType, size_bytes=@sizeBytes, checksum_sha256=@checksum, status=@status, elapsed_ms=@elapsedMs
WHERE tenant_id=@tenantId AND id=@itemId;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, itemId, documentId, versionId, storedFileName, contentType, sizeBytes, checksum, status, elapsedMs }, cancellationToken: ct));
    }

    private async Task UpdateItemStatusAsync(Guid tenantId, Guid itemId, string status, long elapsedMs, CancellationToken ct)
    {
        const string sql = "UPDATE ged.upload_batch_item SET status=@status, finished_at=CASE WHEN @status IN ('COMPLETED','ERROR','SKIPPED','CANCELLED') THEN now() ELSE finished_at END, elapsed_ms=@elapsedMs WHERE tenant_id=@tenantId AND id=@itemId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, itemId, status, elapsedMs }, cancellationToken: ct));
    }

    private async Task MarkItemSkippedAsync(Guid tenantId, Guid itemId, string message, long elapsedMs, CancellationToken ct)
    {
        const string sql = "UPDATE ged.upload_batch_item SET status='SKIPPED', error_message=@message, can_retry=false, finished_at=now(), elapsed_ms=@elapsedMs WHERE tenant_id=@tenantId AND id=@itemId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, itemId, message, elapsedMs }, cancellationToken: ct));
    }

    private async Task MarkItemErrorAsync(Guid tenantId, Guid itemId, string message, string step, bool canRetry, long elapsedMs, CancellationToken ct)
    {
        const string sql = "UPDATE ged.upload_batch_item SET status='ERROR', error_message=@message, error_step=@step, can_retry=@canRetry, finished_at=now(), elapsed_ms=@elapsedMs WHERE tenant_id=@tenantId AND id=@itemId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, itemId, message, step, canRetry, elapsedMs }, cancellationToken: ct));
    }

    private async Task RefreshBatchCountersAsync(Guid tenantId, Guid batchId, bool finished, CancellationToken ct)
    {
        const string sql = """
WITH c AS (
  SELECT count(*)::int total,
         count(*) FILTER (WHERE status='COMPLETED')::int success,
         count(*) FILTER (WHERE status='ERROR')::int failed,
         count(*) FILTER (WHERE status='SKIPPED')::int skipped,
         count(*) FILTER (WHERE status IN ('PENDING','RECEIVING','SAVED','DOCUMENT_CREATED','OCR_QUEUED','PREVIEW_QUEUED','QUEUED'))::int pending
  FROM ged.upload_batch_item WHERE tenant_id=@tenantId AND batch_id=@batchId AND reg_status='A'
)
UPDATE ged.upload_batch b
SET success_files=c.success, failed_files=c.failed, skipped_files=c.skipped,
    status=CASE WHEN @finished THEN CASE WHEN c.failed>0 AND (c.success>0 OR c.skipped>0) THEN 'PARTIAL_ERROR' WHEN c.failed>0 THEN 'ERROR' ELSE 'COMPLETED' END ELSE b.status END,
    finished_at=CASE WHEN @finished THEN now() ELSE b.finished_at END
FROM c
WHERE b.tenant_id=@tenantId AND b.id=@batchId;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, batchId, finished }, cancellationToken: ct));
    }

    private sealed class BatchStatusRow { public Guid? RequestedFolderId { get; set; } public Guid? ResolvedFolderId { get; set; } public string? FolderName { get; set; } public string Status { get; set; } = "OPEN"; public int TotalFiles { get; set; } public int SuccessFiles { get; set; } public int FailedFiles { get; set; } public int SkippedFiles { get; set; } }

    private async Task<UploadBatchStatusDto> LoadStatusAsync(Guid tenantId, Guid batchId, bool includeAllItems, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var batch = await conn.QuerySingleOrDefaultAsync<BatchStatusRow>(new CommandDefinition("""
SELECT b.requested_folder_id AS RequestedFolderId, b.folder_id AS ResolvedFolderId, f.name AS FolderName,
       b.status, b.total_files AS TotalFiles, b.success_files AS SuccessFiles, b.failed_files AS FailedFiles, b.skipped_files AS SkippedFiles
FROM ged.upload_batch b
LEFT JOIN ged.folder f ON f.tenant_id=b.tenant_id AND f.id=b.folder_id AND COALESCE(f.reg_status, 'A')='A'
WHERE b.tenant_id=@tenantId AND b.id=@batchId AND b.reg_status='A';
""", new { tenantId, batchId }, cancellationToken: ct)) ?? new BatchStatusRow();
        var items = (await conn.QueryAsync<UploadBatchItemStatusDto>(new CommandDefinition("""
SELECT id, original_file_name AS OriginalFileName, stored_file_name AS StoredFileName, document_id AS DocumentId, version_id AS VersionId, status, error_message AS ErrorMessage, error_step AS ErrorStep, can_retry AS CanRetry, size_bytes AS SizeBytes, correlation_id AS CorrelationId
FROM ged.upload_batch_item
WHERE tenant_id=@tenantId AND batch_id=@batchId AND reg_status='A'
ORDER BY created_at, original_file_name;
""", new { tenantId, batchId }, cancellationToken: ct))).AsList();
        var pending = Math.Max(0, batch.TotalFiles - items.Count(x => x.Status is "COMPLETED" or "ERROR" or "SKIPPED"));
        var created = items
            .Where(x => x.Status == "COMPLETED" && x.DocumentId.HasValue)
            .Select(x => new UploadBatchCreatedDocumentDto
            {
                DocumentId = x.DocumentId,
                VersionId = x.VersionId,
                FileName = x.StoredFileName ?? x.OriginalFileName,
                Title = Path.GetFileNameWithoutExtension(x.StoredFileName ?? x.OriginalFileName)
            })
            .ToList();
        return new UploadBatchStatusDto { BatchId = batchId, RequestedFolderId = batch.RequestedFolderId, ResolvedFolderId = batch.ResolvedFolderId, FolderName = batch.FolderName, CreatedDocuments = created, Status = batch.Status ?? "OPEN", Total = batch.TotalFiles, Success = items.Count(x => x.Status == "COMPLETED"), Failed = items.Count(x => x.Status == "ERROR"), Skipped = items.Count(x => x.Status == "SKIPPED"), Pending = pending, Items = includeAllItems ? items : Array.Empty<UploadBatchItemStatusDto>(), Errors = items.Where(x => x.Status == "ERROR").ToList() };
    }
}
