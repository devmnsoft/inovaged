using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Documents.Partials;

public sealed class AddDocumentPartRequest
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid? PartialGroupId { get; init; }
    public int PartNumber { get; init; }
    public int? TotalParts { get; init; }
    public string? FileName { get; init; }
    public long? SizeBytes { get; init; }
    public string? Notes { get; init; }
    public DateTime UploadedAtUtc { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

public sealed class DocumentPartialPartDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid PartialGroupId { get; init; }
    public int PartNumber { get; init; }
    public int? TotalParts { get; init; }
    public string? FileName { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime UploadedAtUtc { get; init; }
    public Guid? UploadedBy { get; init; }
    public string? UploadedByName { get; init; }
    public string Status { get; init; } = "UPLOADED";
    public string? Notes { get; init; }
    public string? OcrStatus { get; init; }
    public bool HasOcrText { get; init; }
    public bool IsOcrAvailable { get; init; }
}

public sealed record DocumentPartialSummaryDto
{
    public Guid DocumentId { get; init; }
    public Guid PartialGroupId { get; init; }
    public string PartialStatus { get; init; } = "NOT_PARTIAL";
    public int PartsCount { get; init; }
    public int? TotalParts { get; init; }
    public Guid? ConsolidatedVersionId { get; init; }
    public bool CanConsolidate { get; init; }
}

public interface IDocumentPartialService
{
    Task<Result<DocumentPartialSummaryDto>> AddPartAsync(AddDocumentPartRequest request, CancellationToken ct);
    Task<IReadOnlyList<DocumentPartialPartDto>> GetPartsAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<Result<DocumentPartialSummaryDto>> ConsolidateAsync(Guid tenantId, Guid userId, Guid documentId, string? correlationId, CancellationToken ct);
    Task<Result<DocumentPartialSummaryDto>> CancelPartialAsync(Guid tenantId, Guid userId, Guid documentId, string? reason, string? correlationId, CancellationToken ct);
    Task<Result<DocumentPartialSummaryDto>> MarkAsCompleteAsync(Guid tenantId, Guid userId, Guid documentId, string? correlationId, CancellationToken ct);
}
