namespace InovaGed.Application.Ged.Documents;

public static class DocumentIntakeReviewStatus
{
    public const string Pending = "PENDING";
    public const string Reviewed = "REVIEWED";
    public const string NeedsCorrection = "NEEDS_CORRECTION";
}

public sealed record DocumentIntakeReviewDto(Guid DocumentId, string Status, Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt, string? Notes);

public interface IDocumentIntakeReviewService
{
    Task<DocumentIntakeReviewDto> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, DocumentIntakeReviewDto>> GetForDocumentsAsync(Guid tenantId,
        IReadOnlyCollection<Guid> documentIds, CancellationToken ct);
    Task<DocumentIntakeReviewDto> MarkReviewedAsync(Guid tenantId, Guid userId, Guid documentId,
        string? notes, CancellationToken ct);
    Task<DocumentIntakeReviewDto> MarkNeedsCorrectionAsync(Guid tenantId, Guid userId, Guid documentId,
        string reason, CancellationToken ct);
    Task<DocumentIntakeReviewDto> ResetPendingAsync(Guid tenantId, Guid userId, Guid documentId,
        CancellationToken ct);
}
