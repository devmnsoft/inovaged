namespace InovaGed.Web.Models.Ged;

/// <summary>Presentation-only state consumed by the document badge partial.</summary>
public sealed record DocumentBadgesVM(
    Guid DocumentId,
    string? Status,
    string? OcrStatus,
    string? PreviewStatus,
    string? ClassificationStatus,
    string? RetentionStatus,
    string? SignatureStatus,
    bool IsPartial,
    bool IsRestricted,
    bool IsOverdue,
    bool ShowOcrBadge,
    bool ShowPreviewBadge,
    bool ShowClassificationBadge,
    bool ShowRetentionBadge,
    bool ShowSignatureBadge)
{
    public static DocumentBadgesVM FromDocument(GedExplorerVM.DocumentRowVM document) => new(
        document.Id,
        document.IsDocumentIncomplete ? "INCOMPLETE" : "ACTIVE",
        document.IsOcrAvailable || document.HasOcrText ? "COMPLETED" : document.OcrStatus,
        null,
        document.ClassificationId.HasValue ? "CLASSIFIED" : "UNCLASSIFIED",
        null,
        null,
        document.IsPartialDocument || document.IsDocumentIncomplete,
        document.IsConfidential,
        false,
        true,
        false,
        true,
        false,
        false);
}
