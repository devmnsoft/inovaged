using InovaGed.Application.Audit;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class DocumentBulkUploadService : IDocumentBulkUploadService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".doc", ".docx", ".xls", ".xlsx", ".txt" };
    private readonly long _maxFileSizeBytes;
    private readonly DocumentAppService _documentApp; private readonly IAuditWriter _audit; private readonly ILogger<DocumentBulkUploadService> _logger;
    public DocumentBulkUploadService(DocumentAppService documentApp, IAuditWriter audit, ILogger<DocumentBulkUploadService> logger, IConfiguration configuration){_documentApp=documentApp;_audit=audit;_logger=logger;var maxFileSizeMb=Math.Max(1, configuration.GetValue<int?>("DocumentUpload:MaxFileSizeMb") ?? 50); _maxFileSizeBytes=maxFileSizeMb*1024L*1024L;}
    public async Task<Result<DocumentBulkUploadResultDto>> UploadSingleAsync(Guid tenantId, Guid userId, string? userName, IFormFile file, Guid? folderId, DocumentBulkUploadMetadata metadata, bool isAdmin, CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            if (!folderId.HasValue || folderId == Guid.Empty) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", "Selecione uma pasta destino.");
            if (file is null || file.Length <= 0) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", "Arquivo inválido.");
            if (file.Length > _maxFileSizeBytes) return Result<DocumentBulkUploadResultDto>.Fail("LIMIT", $"O arquivo {file.FileName} excede o tamanho máximo permitido.");
            var ext = Path.GetExtension(file.FileName ?? string.Empty); if (!AllowedExtensions.Contains(ext)) return Result<DocumentBulkUploadResultDto>.Fail("VALIDATION", $"Extensão não permitida: {ext}");
            var safeName = Path.GetFileName(string.IsNullOrWhiteSpace(metadata.UploadName) ? file.FileName : metadata.UploadName);
            await using var stream = file.OpenReadStream();
            var cmd = new UploadDocumentCommand { FolderId = folderId.Value, TypeId = metadata.DocumentTypeId, ClassificationId = metadata.ClassificationId, Description = metadata.Notes, Visibility = string.IsNullOrWhiteSpace(metadata.Visibility) ? "INTERNAL" : metadata.Visibility.Trim().ToUpperInvariant(), Title = Path.GetFileNameWithoutExtension(safeName), FileName = safeName, ContentType = file.ContentType, Content = stream };
            var result = await _documentApp.UploadAsync(cmd, "BULK", userName ?? "BULK", ct);
            if (!result.Success) return Result<DocumentBulkUploadResultDto>.Fail(result.Error?.Code ?? "UPLOAD", result.Error?.Message ?? "Falha no upload.");
            sw.Stop();
            _logger.LogInformation("Upload finalizado. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} File={FileName} FileSize={FileSize} ContentType={ContentType} DuplicateStrategy={DuplicateStrategy} OcrQueued={OcrQueued} PreviewQueued={PreviewQueued} DocumentId={DocumentId} ElapsedMs={ElapsedMs}",
                tenantId, userId, folderId, metadata.BatchId, safeName, file.Length, file.ContentType, metadata.DuplicateStrategy, metadata.RunOcr, metadata.GeneratePreview, result.Value, sw.ElapsedMilliseconds);
            await _audit.WriteAsync(tenantId, userId, "UPLOAD", "DOCUMENT", result.Value, "Upload em lote concluído", null, null, new { folderId, fileName = safeName, fileSize = file.Length, file.ContentType, metadata.RunOcr, metadata.GeneratePreview, metadata.BatchId }, ct);
            return Result<DocumentBulkUploadResultDto>.Ok(new DocumentBulkUploadResultDto { DocumentId = result.Value, FileName = safeName });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em UploadSingleAsync. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} File={FileName}", tenantId, userId, folderId, metadata.BatchId, file?.FileName);
            return Result<DocumentBulkUploadResultDto>.Fail("ERR", "Erro interno ao enviar arquivo.");
        }
    }
    public async Task<Result<BulkUploadBatchResultDto>> FinishBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct){await _audit.WriteAsync(tenantId,userId,"BATCH_EVENT","DOCUMENT_BULK_UPLOAD",batchId,"Lote de upload finalizado",null,null,new { batchId },ct); return Result<BulkUploadBatchResultDto>.Ok(new BulkUploadBatchResultDto { BatchId = batchId });}
}
