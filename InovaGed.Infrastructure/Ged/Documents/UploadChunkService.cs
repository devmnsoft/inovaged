using System.Diagnostics;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ocr;
using InovaGed.Application.Preview;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class UploadChunkService : IUploadChunkService
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentBulkUploadService _bulkUpload;
    private readonly IUploadConcurrencyLimiter _limiter;
    private readonly IOcrJobRepository _ocrJobs;
    private readonly IPreviewJobQueue _previewQueue;
    private readonly IPreviewStatusRepository _previewStatus;
    private readonly DocumentUploadOptions _options;
    private readonly ILogger<UploadChunkService> _logger;
    private readonly HashSet<string> _allowedExtensions;
    private readonly string _root;

    public UploadChunkService(IDbConnectionFactory db, IDocumentBulkUploadService bulkUpload, IUploadConcurrencyLimiter limiter, IOcrJobRepository ocrJobs, IPreviewJobQueue previewQueue, IPreviewStatusRepository previewStatus, IOptions<DocumentUploadOptions> options, ILogger<UploadChunkService> logger)
    {
        _db = db;
        _bulkUpload = bulkUpload;
        _limiter = limiter;
        _ocrJobs = ocrJobs;
        _previewQueue = previewQueue;
        _previewStatus = previewStatus;
        _options = options.Value;
        _logger = logger;
        _allowedExtensions = new HashSet<string>(_options.AllowedExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _root = Path.Combine(AppContext.BaseDirectory, "App_Data", "GedChunkUploads");
    }

    public async Task<Result<UploadChunkSessionDto>> StartAsync(Guid tenantId, Guid userId, bool isAdmin, string? userName, StartUploadChunkRequestDto request, CancellationToken ct)
    {
        if (!request.FolderId.HasValue || request.FolderId.Value == Guid.Empty) return Result<UploadChunkSessionDto>.Fail("VALIDATION", "Selecione uma pasta para enviar documentos.");
        if (request.TotalSizeBytes <= 0) return Result<UploadChunkSessionDto>.Fail("VALIDATION", "Tamanho de arquivo inválido.");
        if (request.TotalSizeBytes > Math.Max(1, _options.MaxFileSizeMb) * 1024L * 1024L) return Result<UploadChunkSessionDto>.Fail("LIMIT", $"O arquivo excede o limite configurado de {_options.MaxFileSizeMb} MB.");
        var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(request.UploadName) ? request.OriginalFileName : request.UploadName);
        var ext = Path.GetExtension(safeName ?? string.Empty);
        if (_allowedExtensions.Count > 0 && !_allowedExtensions.Contains(ext)) return Result<UploadChunkSessionDto>.Fail("EXTENSION", $"Extensão não permitida: {ext}");

        var chunkSize = request.ChunkSizeBytes.GetValueOrDefault(Math.Max(1, _options.ChunkSizeMb) * 1024 * 1024);
        var totalChunks = request.TotalChunks.GetValueOrDefault((int)Math.Ceiling(request.TotalSizeBytes / (double)chunkSize));
        var uploadId = Guid.NewGuid();
        var tempPath = Path.Combine(_root, tenantId.ToString("N"), uploadId.ToString("N"));
        Directory.CreateDirectory(tempPath);
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId;
        var itemId = Guid.NewGuid();
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            request.FileIndex,
            request.TotalFiles,
            request.DuplicateStrategy,
            request.RunOcr,
            request.GeneratePreview,
            request.FolderName,
            request.ExistingDocumentId,
            request.UploadName,
            UserName = userName,
            IsAdmin = isAdmin,
            request.Metadata.DocumentTypeId,
            request.Metadata.ClassificationId,
            request.Metadata.Notes,
            request.Metadata.Visibility,
            request.Metadata.IsDocumentPart,
            request.Metadata.PartNumber,
            request.Metadata.TotalParts,
            request.Metadata.ConsolidateIntoDocumentId,
            BatchItemId = itemId
        });

        const string sql = """
INSERT INTO ged.upload_session (id, tenant_id, user_id, batch_id, batch_item_id, folder_id, requested_folder_id, original_file_name, content_type, total_size_bytes, chunk_size_bytes, total_chunks, received_chunks, status, temp_path, metadata_json, created_at, updated_at, correlation_id)
VALUES (@uploadId, @tenantId, @userId, @batchId, @itemId, @folderId, @requestedFolderId, @fileName, @contentType, @totalSizeBytes, @chunkSize, @totalChunks, 0, 'OPEN', @tempPath, CAST(@metadataJson AS jsonb), now(), now(), @correlationId);
INSERT INTO ged.upload_batch_item (id, tenant_id, batch_id, folder_id, requested_folder_id, upload_session_id, original_file_name, content_type, size_bytes, status, started_at, attempt, correlation_id)
SELECT @itemId, @tenantId, @batchId, @folderId, @requestedFolderId, @fileName, @contentType, @totalSizeBytes, 'RECEIVING', now(), 1, @correlationId
WHERE @batchId IS NOT NULL;
UPDATE ged.upload_batch SET status='PROCESSING', started_at=COALESCE(started_at, now()) WHERE tenant_id=@tenantId AND id=@batchId AND status='OPEN';
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { uploadId, tenantId, userId, batchId = request.BatchId, itemId, folderId = request.FolderId, requestedFolderId = request.RequestedFolderId ?? request.FolderId, fileName = safeName, request.ContentType, totalSizeBytes = request.TotalSizeBytes, chunkSize, totalChunks, tempPath, metadataJson, correlationId }, cancellationToken: ct));
            _logger.LogInformation("Chunk upload session start Tenant={TenantId} User={UserId} Upload={UploadId} Batch={BatchId} File={FileName} Size={SizeBytes} Chunks={TotalChunks} CorrelationId={CorrelationId}", tenantId, userId, uploadId, request.BatchId, safeName, request.TotalSizeBytes, totalChunks, correlationId);
            return Result<UploadChunkSessionDto>.Ok(new UploadChunkSessionDto { UploadId = uploadId, BatchId = request.BatchId, ChunkSizeBytes = chunkSize, TotalChunks = totalChunks, NextChunk = 0, MissingChunks = Enumerable.Range(0, totalChunks).ToArray(), Status = "OPEN", CorrelationId = correlationId });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable || ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            _logger.LogError(ex, "Tabelas de upload chunked ausentes. Execute a migration 20260603_upload_chunk.sql. Tenant={TenantId} User={UserId}", tenantId, userId);
            return Result<UploadChunkSessionDto>.Fail("UPLOAD_CHUNK_SCHEMA_MISSING", "Upload em partes indisponível. Estrutura de banco pendente.");
        }
    }

    public async Task<Result<UploadChunkStatusDto>> SavePartAsync(Guid tenantId, Guid userId, UploadChunkPartRequestDto request, CancellationToken ct)
    {
        var session = await LoadSessionAsync(tenantId, userId, request.UploadId, ct);
        if (session is null) return Result<UploadChunkStatusDto>.Fail("NOT_FOUND", "Sessão de upload não encontrada.");
        if (request.ChunkIndex < 0 || request.ChunkIndex >= session.TotalChunks) return Result<UploadChunkStatusDto>.Fail("VALIDATION", "Parte inválida.");
        Directory.CreateDirectory(session.TempPath);
        var chunkPath = Path.Combine(session.TempPath, $"chunk-{request.ChunkIndex:D8}.part");
        await using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await request.Content.CopyToAsync(fs, ct);
        }

        const string sql = """
INSERT INTO ged.upload_session_chunk (id, session_id, chunk_index, size_bytes, checksum_sha256, received_at, temp_path)
VALUES (gen_random_uuid(), @uploadId, @chunkIndex, @sizeBytes, @checksum, now(), @chunkPath)
ON CONFLICT (session_id, chunk_index) DO UPDATE SET size_bytes=EXCLUDED.size_bytes, checksum_sha256=EXCLUDED.checksum_sha256, received_at=now(), temp_path=EXCLUDED.temp_path;
UPDATE ged.upload_session s
SET received_chunks=(SELECT count(*) FROM ged.upload_session_chunk c WHERE c.session_id=s.id), status=CASE WHEN status='OPEN' THEN 'RECEIVING' ELSE status END, updated_at=now()
WHERE s.tenant_id=@tenantId AND s.id=@uploadId;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, request.UploadId, request.ChunkIndex, request.SizeBytes, checksum = request.ChecksumSha256, chunkPath }, cancellationToken: ct));
        _logger.LogInformation("Chunk received Tenant={TenantId} User={UserId} Upload={UploadId} Chunk={ChunkIndex} Size={SizeBytes} CorrelationId={CorrelationId}", tenantId, userId, request.UploadId, request.ChunkIndex, request.SizeBytes, request.CorrelationId);
        return await GetStatusAsync(tenantId, userId, request.UploadId, ct);
    }

    public async Task<Result<UploadBatchFileResultDto>> CompleteAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct)
    {
        var session = await LoadSessionAsync(tenantId, userId, uploadId, ct);
        if (session is null) return Result<UploadBatchFileResultDto>.Fail("NOT_FOUND", "Sessão de upload não encontrada.");
        var status = await BuildStatusAsync(session, ct);
        if (status.MissingChunks.Count > 0) return Result<UploadBatchFileResultDto>.Fail("MISSING_CHUNKS", "Ainda existem partes pendentes para concluir o upload.");
        var sw = Stopwatch.StartNew();
        await using var lease = await _limiter.AcquireAsync(tenantId, userId, session.BatchId ?? uploadId, ct);
        if (lease is null) return Result<UploadBatchFileResultDto>.Fail("CONCURRENCY", "Há muitos uploads em andamento. Aguarde alguns segundos.");
        _logger.LogInformation("Chunk complete started Tenant={TenantId} User={UserId} Upload={UploadId} Batch={BatchId} CorrelationId={CorrelationId}", tenantId, userId, uploadId, session.BatchId, session.CorrelationId);
        var assembled = Path.Combine(session.TempPath, "assembled.bin");
        await using (var output = new FileStream(assembled, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            for (var i = 0; i < session.TotalChunks; i++)
            {
                var part = Path.Combine(session.TempPath, $"chunk-{i:D8}.part");
                await using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, ct);
            }
        }
        await using var finalStream = new FileStream(assembled, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var md = session.Metadata;
        var bulkMd = new DocumentBulkUploadMetadata { BatchId = session.BatchId, DuplicateStrategy = md.DuplicateStrategy, ExistingDocumentId = md.ExistingDocumentId, UploadName = md.UploadName, DocumentTypeId = md.DocumentTypeId, ClassificationId = md.ClassificationId, Notes = md.Notes, Visibility = md.Visibility, RunOcr = md.RunOcr, GeneratePreview = md.GeneratePreview, IsDocumentPart = md.IsDocumentPart, PartNumber = md.PartNumber, TotalParts = md.TotalParts, ConsolidateIntoDocumentId = md.ConsolidateIntoDocumentId };
        var upload = await _bulkUpload.UploadStreamAsync(tenantId, userId, md.UserName, finalStream, session.OriginalFileName, session.ContentType ?? "application/octet-stream", session.TotalSizeBytes, session.FolderId, bulkMd, md.IsAdmin, ct);
        if (!upload.Success)
        {
            await MarkFailedAsync(tenantId, session, upload.Error?.Message ?? "Falha ao persistir arquivo.", upload.Error?.Code ?? "Persistência", sw.ElapsedMilliseconds, ct);
            return Result<UploadBatchFileResultDto>.Fail(upload.Error?.Code ?? "UPLOAD", upload.Error?.Message ?? "Falha ao concluir upload.");
        }
        var version = await GetCurrentVersionAsync(tenantId, upload.Value!.DocumentId, ct);
        var ocrQueued = false;
        var previewQueued = false;
        if (md.RunOcr && version?.VersionId is Guid ocrVersionId) { await _ocrJobs.EnqueueAsync(tenantId, ocrVersionId, userId, false, ct); ocrQueued = true; _logger.LogInformation("OCR queued Tenant={TenantId} User={UserId} Upload={UploadId} VersionId={VersionId} CorrelationId={CorrelationId}", tenantId, userId, uploadId, ocrVersionId, session.CorrelationId); }
        if (md.GeneratePreview && version is not null) { await _previewStatus.UpsertAsync(tenantId, version.VersionId, PreviewProcessingStatus.Pending, null, null, DateTimeOffset.UtcNow, null, ct); await _previewQueue.EnqueueAsync(tenantId, upload.Value.DocumentId, version.VersionId, version.StoragePath, version.FileName, ct); previewQueued = true; _logger.LogInformation("Preview queued Tenant={TenantId} User={UserId} Upload={UploadId} VersionId={VersionId} CorrelationId={CorrelationId}", tenantId, userId, uploadId, version.VersionId, session.CorrelationId); }
        await MarkCompletedAsync(tenantId, session, upload.Value.DocumentId, version?.VersionId, version?.FileName, version?.ChecksumSha256, sw.ElapsedMilliseconds, ct);
        _logger.LogInformation("Chunk complete finished Tenant={TenantId} User={UserId} Upload={UploadId} DocumentId={DocumentId} VersionId={VersionId} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}", tenantId, userId, uploadId, upload.Value.DocumentId, version?.VersionId, sw.ElapsedMilliseconds, session.CorrelationId);
        return Result<UploadBatchFileResultDto>.Ok(new UploadBatchFileResultDto { ItemId = session.BatchItemId ?? uploadId, DocumentId = upload.Value.DocumentId, VersionId = version?.VersionId, RequestedFolderId = session.RequestedFolderId, ResolvedFolderId = session.FolderId, FolderName = md.FolderName, Title = upload.Value.Title, FileName = upload.Value.FileName, UploadedAtUtc = version?.UploadedAtUtc, UploadedAtLocalFormatted = version?.UploadedAtUtc?.ToUniversalTime().ToString("dd/MM/yyyy HH:mm"), Status = "COMPLETED", Message = "Arquivo grande recebido em partes com sucesso.", OcrQueued = ocrQueued, PreviewQueued = previewQueued, CorrelationId = session.CorrelationId });
    }

    public async Task<Result<UploadChunkStatusDto>> GetStatusAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct)
    {
        var session = await LoadSessionAsync(tenantId, userId, uploadId, ct);
        return session is null ? Result<UploadChunkStatusDto>.Fail("NOT_FOUND", "Sessão de upload não encontrada.") : Result<UploadChunkStatusDto>.Ok(await BuildStatusAsync(session, ct));
    }

    public async Task<Result<UploadChunkStatusDto>> CancelAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct)
    {
        const string sql = "UPDATE ged.upload_session SET status='CANCELLED', updated_at=now(), error_message='Cancelado pelo usuário.' WHERE tenant_id=@tenantId AND user_id=@userId AND id=@uploadId; UPDATE ged.upload_batch_item SET status='CANCELLED', finished_at=now(), error_message='Cancelado pelo usuário.' WHERE tenant_id=@tenantId AND upload_session_id=@uploadId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, uploadId }, cancellationToken: ct));
        _logger.LogInformation("Chunk upload cancelled Tenant={TenantId} User={UserId} Upload={UploadId}", tenantId, userId, uploadId);
        return await GetStatusAsync(tenantId, userId, uploadId, ct);
    }

    private async Task<SessionRow?> LoadSessionAsync(Guid tenantId, Guid userId, Guid uploadId, CancellationToken ct)
    {
        const string sql = """
SELECT id AS UploadId, tenant_id AS TenantId, user_id AS UserId, batch_id AS BatchId, batch_item_id AS BatchItemId, folder_id AS FolderId, requested_folder_id AS RequestedFolderId, original_file_name AS OriginalFileName, content_type AS ContentType, total_size_bytes AS TotalSizeBytes, chunk_size_bytes AS ChunkSizeBytes, total_chunks AS TotalChunks, received_chunks AS ReceivedChunks, status, temp_path AS TempPath, document_id AS DocumentId, version_id AS VersionId, error_message AS ErrorMessage, metadata_json::text AS MetadataJson, correlation_id AS CorrelationId
FROM ged.upload_session WHERE tenant_id=@tenantId AND user_id=@userId AND id=@uploadId;
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition(sql, new { tenantId, userId, uploadId }, cancellationToken: ct));
    }

    private async Task<UploadChunkStatusDto> BuildStatusAsync(SessionRow session, CancellationToken ct)
    {
        const string sql = "SELECT chunk_index FROM ged.upload_session_chunk WHERE session_id=@uploadId ORDER BY chunk_index;";
        await using var conn = await _db.OpenAsync(ct);
        var received = (await conn.QueryAsync<int>(new CommandDefinition(sql, new { session.UploadId }, cancellationToken: ct))).AsList();
        var set = received.ToHashSet();
        var missing = Enumerable.Range(0, session.TotalChunks).Where(i => !set.Contains(i)).ToArray();
        return new UploadChunkStatusDto { UploadId = session.UploadId, BatchId = session.BatchId, Status = session.Status, TotalChunks = session.TotalChunks, ReceivedChunksCount = received.Count, ReceivedChunks = received, MissingChunks = missing, Percent = session.TotalChunks == 0 ? 0 : Math.Round(received.Count * 100d / session.TotalChunks, 2), DocumentId = session.DocumentId, VersionId = session.VersionId, ErrorMessage = session.ErrorMessage };
    }

    private async Task MarkCompletedAsync(Guid tenantId, SessionRow session, Guid documentId, Guid? versionId, string? storedFileName, string? checksum, long elapsedMs, CancellationToken ct)
    {
        const string sql = """
UPDATE ged.upload_session SET status='COMPLETED', document_id=@documentId, version_id=@versionId, completed_at=now(), updated_at=now() WHERE tenant_id=@tenantId AND id=@uploadId;
UPDATE ged.upload_batch_item SET document_id=@documentId, version_id=@versionId, stored_file_name=@storedFileName, checksum_sha256=@checksum, status='COMPLETED', finished_at=now(), elapsed_ms=@elapsedMs WHERE tenant_id=@tenantId AND upload_session_id=@uploadId;
WITH c AS (SELECT batch_id, count(*) FILTER (WHERE status='COMPLETED')::int success, count(*) FILTER (WHERE status='ERROR')::int failed, count(*) FILTER (WHERE status='SKIPPED')::int skipped FROM ged.upload_batch_item WHERE tenant_id=@tenantId AND batch_id=@batchId AND reg_status='A' GROUP BY batch_id)
UPDATE ged.upload_batch b SET success_files=c.success, failed_files=c.failed, skipped_files=c.skipped FROM c WHERE b.tenant_id=@tenantId AND b.id=c.batch_id;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, session.UploadId, session.BatchId, documentId, versionId, storedFileName, checksum, elapsedMs }, cancellationToken: ct));
    }

    private async Task MarkFailedAsync(Guid tenantId, SessionRow session, string message, string step, long elapsedMs, CancellationToken ct)
    {
        const string sql = "UPDATE ged.upload_session SET status='ERROR', error_message=@message, updated_at=now() WHERE tenant_id=@tenantId AND id=@uploadId; UPDATE ged.upload_batch_item SET status='ERROR', error_message=@message, error_step=@step, can_retry=true, finished_at=now(), elapsed_ms=@elapsedMs WHERE tenant_id=@tenantId AND upload_session_id=@uploadId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, session.UploadId, message, step, elapsedMs }, cancellationToken: ct));
        _logger.LogWarning("Chunk upload failed Tenant={TenantId} Upload={UploadId} Step={Step} Message={Message}", tenantId, session.UploadId, step, message);
    }

    private async Task<VersionInfo?> GetCurrentVersionAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
SELECT v.id AS VersionId, v.file_name AS FileName, v.storage_path AS StoragePath, COALESCE(v.uploaded_at_utc, v.created_at) AS UploadedAtUtc, v.checksum_sha256 AS ChecksumSha256
FROM ged.document d JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=d.current_version_id
WHERE d.tenant_id=@tenantId AND d.id=@documentId;
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<VersionInfo>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    private sealed class SessionRow
    {
        public Guid UploadId { get; set; }
        public Guid? BatchId { get; set; }
        public Guid? BatchItemId { get; set; }
        public Guid FolderId { get; set; }
        public Guid? RequestedFolderId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long TotalSizeBytes { get; set; }
        public int ChunkSizeBytes { get; set; }
        public int TotalChunks { get; set; }
        public string Status { get; set; } = string.Empty;
        public string TempPath { get; set; } = string.Empty;
        public Guid? DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string? ErrorMessage { get; set; }
        public string MetadataJson { get; set; } = "{}";
        public string? CorrelationId { get; set; }
        public Metadata Metadata => System.Text.Json.JsonSerializer.Deserialize<Metadata>(MetadataJson) ?? new Metadata();
    }

    private sealed class Metadata
    {
        public string? DuplicateStrategy { get; set; }
        public bool RunOcr { get; set; }
        public bool GeneratePreview { get; set; }
        public string? FolderName { get; set; }
        public Guid? ExistingDocumentId { get; set; }
        public string? UploadName { get; set; }
        public string? UserName { get; set; }
        public bool IsAdmin { get; set; }
        public Guid? DocumentTypeId { get; set; }
        public Guid? ClassificationId { get; set; }
        public string? Notes { get; set; }
        public string? Visibility { get; set; }
        public bool IsDocumentPart { get; set; }
        public int? PartNumber { get; set; }
        public int? TotalParts { get; set; }
        public Guid? ConsolidateIntoDocumentId { get; set; }
    }
    private sealed class VersionInfo { public Guid VersionId { get; set; } public string FileName { get; set; } = string.Empty; public string StoragePath { get; set; } = string.Empty; public string? ChecksumSha256 { get; set; } public DateTime? UploadedAtUtc { get; set; } }
}
