using InovaGed.Application.Audit;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Folders;
using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using InovaGed.Infrastructure.Ged;
using System.Diagnostics;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents.Partials;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentBulkUploadService : IDocumentBulkUploadService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" };
    private readonly long _maxFileSizeBytes;
    private readonly DocumentAppService _documentApp; private readonly IAuditWriter _audit; private readonly ILogger<DocumentBulkUploadService> _logger; private readonly IMemoryCache _cache; private readonly IDbConnectionFactory _db; private readonly IDocumentPartialService _partialService;
    public DocumentBulkUploadService(DocumentAppService documentApp, IAuditWriter audit, ILogger<DocumentBulkUploadService> logger, IConfiguration configuration, IMemoryCache cache, IDbConnectionFactory db, IDocumentPartialService partialService){_documentApp=documentApp;_audit=audit;_logger=logger;_cache=cache;_db=db;_partialService=partialService;var maxFileSizeMb=Math.Max(1, configuration.GetValue<int?>("DocumentUpload:MaxFileSizeMb") ?? 50); _maxFileSizeBytes=maxFileSizeMb*1024L*1024L;}
    public async Task<Result<DocumentBulkUploadResultDto>> UploadStreamAsync(Guid tenantId, Guid userId, string? userName, Stream content, string fileName, string contentType, long sizeBytes, Guid? folderId, DocumentBulkUploadMetadata metadata, bool isAdmin, CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            if (!folderId.HasValue || folderId.Value == Guid.Empty) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", "Selecione uma pasta para enviar documentos.");
            if (content is null || content == Stream.Null || sizeBytes <= 0) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", "Arquivo inválido.");
            if (sizeBytes > _maxFileSizeBytes) return Result<DocumentBulkUploadResultDto>.Fail("LIMIT", $"O arquivo {fileName} excede o tamanho máximo configurado.");
            var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(metadata.UploadName) ? fileName : metadata.UploadName);
            var ext = Path.GetExtension(safeName ?? string.Empty);
            if (!AllowedExtensions.Contains(ext)) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", $"Extensão não permitida: {ext}");
            if (content.CanSeek) content.Position = 0;
            var title = Path.GetFileNameWithoutExtension(safeName);
            var uploadedAtUtc = DateTime.UtcNow;
            var isPart = metadata.IsDocumentPart || metadata.PartNumber.HasValue || metadata.TotalParts.HasValue;
            var expectedTotalParts = metadata.TotalParts.GetValueOrDefault();
            var incomingPartNumber = metadata.PartNumber.GetValueOrDefault(isPart ? 1 : 0);
            var isMarkedIncomplete = metadata.MarkAsIncomplete;
            var isIncomplete = isMarkedIncomplete || (isPart && (!metadata.TotalParts.HasValue || incomingPartNumber < expectedTotalParts));
            Guid documentId;
            Guid? versionId = null;
            if (isPart && (metadata.ConsolidateIntoDocumentId.HasValue || metadata.ExistingDocumentId.HasValue) && (metadata.ConsolidateIntoDocumentId ?? metadata.ExistingDocumentId) is Guid existingDoc && existingDoc != Guid.Empty)
            {
                var addVersion = await _documentApp.AddVersionAsync(existingDoc, content, safeName, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, "BULK_PART", userName ?? "BULK", ct);
                if (!addVersion.Success) return Result<DocumentBulkUploadResultDto>.Fail(addVersion.Error?.Code ?? "UPLOAD_PART", addVersion.Error?.Message ?? "Falha ao anexar parte do documento.");
                documentId = existingDoc;
                versionId = addVersion.Value;
                var partsAfterUpload = await CountDocumentPartsAsync(tenantId, documentId, ct) + 1;
                var isConsolidated = metadata.TotalParts.HasValue && partsAfterUpload >= metadata.TotalParts.Value;
                isIncomplete = metadata.MarkAsIncomplete || !isConsolidated;
                var addPart = await _partialService.AddPartAsync(new AddDocumentPartRequest
                {
                    TenantId = tenantId,
                    UserId = userId,
                    DocumentId = documentId,
                    VersionId = versionId.Value,
                    PartNumber = incomingPartNumber <= 0 ? Math.Max(1, partsAfterUpload) : incomingPartNumber,
                    TotalParts = metadata.TotalParts,
                    FileName = safeName,
                    SizeBytes = sizeBytes,
                    Notes = metadata.Notes,
                    UploadedAtUtc = uploadedAtUtc
                }, ct);
                if (!addPart.Success) return Result<DocumentBulkUploadResultDto>.Fail(addPart.Error?.Code ?? "PART", addPart.Error?.Message ?? "Falha ao registrar a parte.");
                if (addPart.Value?.PartialStatus == "COMPLETE" && !isMarkedIncomplete) isIncomplete = false;
            }
            else
            {
                var cmd = new UploadDocumentCommand { FolderId = folderId.Value, TypeId = metadata.DocumentTypeId, ClassificationId = metadata.ClassificationId, Description = metadata.Notes, Visibility = string.IsNullOrWhiteSpace(metadata.Visibility) ? "INTERNAL" : metadata.Visibility.Trim().ToUpperInvariant(), Title = title, FileName = safeName, ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, Content = content, UploadedAtUtc = uploadedAtUtc, IsPartialDocument = isPart, IsDocumentIncomplete = isIncomplete, IncompleteReason = metadata.IncompleteReason, PartNumber = metadata.PartNumber, TotalParts = metadata.TotalParts };
                var result = await _documentApp.UploadAsync(cmd, "BULK", userName ?? "BULK", ct);
                if (!result.Success) return Result<DocumentBulkUploadResultDto>.Fail(result.Error?.Code ?? "UPLOAD", result.Error?.Message ?? "Falha no upload.");
                documentId = result.Value;
                versionId = await GetCurrentVersionIdAsync(tenantId, documentId, ct);
                if (isPart && versionId.HasValue)
                {
                    var addPart = await _partialService.AddPartAsync(new AddDocumentPartRequest
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        DocumentId = documentId,
                        VersionId = versionId.Value,
                        PartNumber = incomingPartNumber <= 0 ? 1 : incomingPartNumber,
                        TotalParts = metadata.TotalParts,
                        FileName = safeName,
                        SizeBytes = sizeBytes,
                        Notes = metadata.Notes,
                        UploadedAtUtc = uploadedAtUtc
                    }, ct);
                    if (!addPart.Success) return Result<DocumentBulkUploadResultDto>.Fail(addPart.Error?.Code ?? "PART", addPart.Error?.Message ?? "Falha ao registrar a parte.");
                    isIncomplete = metadata.MarkAsIncomplete || addPart.Value?.PartialStatus == "INCOMPLETE";
                }
            }
            if (metadata.MarkAsIncomplete && versionId.HasValue)
            {
                await MarkDocumentIncompleteAsync(tenantId, documentId, versionId, metadata.IncompleteReason, ct);
            }
            sw.Stop();
            _logger.LogInformation("Upload stream finalizado. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} File={FileName} FileSize={FileSize} ContentType={ContentType} DocumentId={DocumentId} ElapsedMs={ElapsedMs}",
                tenantId, userId, folderId, metadata.BatchId, safeName, sizeBytes, contentType, documentId, sw.ElapsedMilliseconds);
            await _audit.WriteAsync(tenantId, userId, isPart ? "UPLOAD_DOCUMENT_PART" : "UPLOAD", "DOCUMENT", documentId, isPart ? "Parte de documento anexada" : "Upload em lote concluído", null, null, new { folderId, fileName = safeName, fileSize = sizeBytes, contentType, metadata.RunOcr, metadata.GeneratePreview, metadata.BatchId, versionId, metadata.PartNumber, metadata.TotalParts, metadata.MarkAsIncomplete, metadata.IncompleteReason, isIncomplete }, ct);
            InvalidateGedFolderCache(tenantId, folderId.Value);
            return Result<DocumentBulkUploadResultDto>.Ok(new DocumentBulkUploadResultDto { DocumentId = documentId, VersionId = versionId, FileName = safeName, FolderId = folderId.Value, Title = title });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("UploadStreamAsync cancelado. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} File={FileName}", tenantId, userId, folderId, metadata.BatchId, fileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em UploadStreamAsync. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} File={FileName}", tenantId, userId, folderId, metadata.BatchId, fileName);
            return Result<DocumentBulkUploadResultDto>.Fail("ERR", "Erro interno ao enviar arquivo.");
        }
    }


    private async Task MarkDocumentIncompleteAsync(Guid tenantId, Guid documentId, Guid? versionId, string? reason, CancellationToken ct)
    {
        const string sql = """
UPDATE ged.document
SET is_document_incomplete = true,
    incomplete_reason = COALESCE(@reason, incomplete_reason),
    incomplete_source = 'USER_MARKED',
    updated_at = now()
WHERE tenant_id = @tenantId
  AND id = @documentId;

UPDATE ged.document_version
SET is_document_incomplete = true,
    incomplete_reason = COALESCE(@reason, incomplete_reason),
    incomplete_source = 'USER_MARKED',
    partial_status = CASE WHEN COALESCE(partial_status, 'NOT_PARTIAL') = 'NOT_PARTIAL' THEN 'INCOMPLETE' ELSE partial_status END
WHERE tenant_id = @tenantId
  AND id = @versionId;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, versionId, reason }, cancellationToken: ct));
    }

    private async Task<int> CountDocumentPartsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
SELECT count(*)::int
FROM ged.document_version
WHERE tenant_id=@tenantId
  AND document_id=@documentId
  AND COALESCE(is_partial_document,false)=true;
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    private async Task RegisterDocumentPartAsync(Guid tenantId, Guid documentId, Guid versionId, DateTime uploadedAtUtc, int? partNumber, int? totalParts, bool isConsolidated, CancellationToken ct)
    {
        const string sql = """
INSERT INTO ged.document_part (id, tenant_id, document_id, version_id, part_number, total_parts, uploaded_at_utc, is_consolidated)
VALUES (gen_random_uuid(), @tenantId, @documentId, @versionId, @partNumber, @totalParts, @uploadedAtUtc, @isConsolidated)
ON CONFLICT (tenant_id, document_id, version_id) DO UPDATE
SET part_number=EXCLUDED.part_number,
    total_parts=EXCLUDED.total_parts,
    uploaded_at_utc=EXCLUDED.uploaded_at_utc,
    is_consolidated=EXCLUDED.is_consolidated;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, versionId, partNumber, totalParts, uploadedAtUtc, isConsolidated }, cancellationToken: ct));
    }

    private async Task ConsolidateDocumentPartsAsync(Guid tenantId, Guid documentId, Guid consolidatedVersionId, DateTime uploadedAtUtc, CancellationToken ct)
    {
        const string sql = """
UPDATE ged.document_version
SET is_document_incomplete=CASE WHEN incomplete_source='USER_MARKED' THEN true ELSE false END,
    consolidated_version_id=@consolidatedVersionId
WHERE tenant_id=@tenantId
  AND document_id=@documentId
  AND COALESCE(is_partial_document,false)=true;

UPDATE ged.document_part
SET is_consolidated=true, consolidated_at_utc=@uploadedAtUtc
WHERE tenant_id=@tenantId
  AND document_id=@documentId;
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, consolidatedVersionId, uploadedAtUtc }, cancellationToken: ct));
    }

    private async Task<Guid?> GetCurrentVersionIdAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = "SELECT current_version_id FROM ged.document WHERE tenant_id=@tenantId AND id=@documentId;";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    private async Task MarkPartialVersionAsync(Guid tenantId, Guid documentId, Guid versionId, DateTime uploadedAtUtc, bool isPart, bool isIncomplete, int? partNumber, int? totalParts, Guid? consolidatedVersionId, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.document_version
SET uploaded_at_utc=@uploadedAtUtc, is_partial_document=@isPart, is_document_incomplete=CASE WHEN incomplete_source='USER_MARKED' THEN true ELSE @isIncomplete END, part_number=@partNumber, total_parts=@totalParts, consolidated_version_id=@consolidatedVersionId
WHERE tenant_id=@tenantId AND id=@versionId;
UPDATE ged.document
SET current_version_id=@versionId
WHERE tenant_id=@tenantId AND id=@documentId;";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, versionId, uploadedAtUtc, isPart, isIncomplete, partNumber, totalParts, consolidatedVersionId }, cancellationToken: ct));
    }

    private void InvalidateGedFolderCache(Guid tenantId, Guid folderId)
    {
        _cache.Remove($"GedDocuments:{tenantId}:{folderId}");
        _cache.Remove($"GedFolderSummary:{tenantId}:{folderId}");
        _cache.Remove(FolderQueries.TreeCacheKey(tenantId));
        _logger.LogDebug("Cache GED invalidado após upload. Tenant={TenantId} Folder={FolderId}", tenantId, folderId);
    }

    public async Task<Result<DocumentBulkUploadResultDto>> UploadSingleAsync(Guid tenantId, Guid userId, string? userName, IFormFile file, Guid? folderId, DocumentBulkUploadMetadata metadata, bool isAdmin, CancellationToken ct)
    {
        if (file is null) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", "Arquivo inválido.");
        await using var stream = file.OpenReadStream();
        return await UploadStreamAsync(tenantId, userId, userName, stream, file.FileName, file.ContentType, file.Length, folderId, metadata, isAdmin, ct);
    }
    public async Task<Result<BulkUploadBatchResultDto>> FinishBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct){await _audit.WriteAsync(tenantId,userId,"BATCH_EVENT","DOCUMENT_BULK_UPLOAD",batchId,"Lote de upload finalizado",null,null,new { batchId },ct); return Result<BulkUploadBatchResultDto>.Ok(new BulkUploadBatchResultDto { BatchId = batchId });}
}
