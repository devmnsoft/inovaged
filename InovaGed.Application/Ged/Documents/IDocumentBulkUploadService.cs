using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Application.Ged.Documents;

public sealed class DocumentBulkUploadMetadata
{
    public Guid? DocumentTypeId { get; set; }
    public Guid? ClassificationId { get; set; }
    public string? Notes { get; set; }
    public string? Visibility { get; set; }
    public bool RunOcr { get; set; }
    public bool GeneratePreview { get; set; }
    public Guid? BatchId { get; set; }
    public string? DuplicateStrategy { get; set; }
    public Guid? ExistingDocumentId { get; set; }
    public string? UploadName { get; set; }
}

public sealed class DocumentBulkUploadResultDto
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? Title { get; set; }
}

public sealed class BulkUploadBatchResultDto
{
    public Guid BatchId { get; set; }
    public int TotalFiles { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
}

public interface IDocumentBulkUploadService
{
    Task<Result<DocumentBulkUploadResultDto>> UploadStreamAsync(Guid tenantId, Guid userId, string? userName, Stream content, string fileName, string contentType, long sizeBytes, Guid? folderId, DocumentBulkUploadMetadata metadata, bool isAdmin, CancellationToken ct);
    Task<Result<DocumentBulkUploadResultDto>> UploadSingleAsync(Guid tenantId, Guid userId, string? userName, IFormFile file, Guid? folderId, DocumentBulkUploadMetadata metadata, bool isAdmin, CancellationToken ct);
    Task<Result<BulkUploadBatchResultDto>> FinishBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
}
