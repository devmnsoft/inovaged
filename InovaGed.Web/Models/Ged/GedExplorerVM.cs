using System;
using System.Collections.Generic;

namespace InovaGed.Web.Models.Ged;

public sealed class GedExplorerVM
{
    public Guid? CurrentFolderId { get; set; }
    public bool CanBulkUpload { get; set; }
    public bool CurrentFolderIsVirtual { get; set; }
    public Guid? CurrentListingFolderId { get; set; }
    public Guid? CurrentUploadFolderId { get; set; }
    public string CurrentFolderName { get; set; } = "";

    public Guid? FolderId { get; set; }
    public string? Query { get; set; }

    public List<FolderNodeVM> Folders { get; set; } = new();
    public List<DocumentRowVM> Documents { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalDocuments { get; set; }
    public bool HasMoreDocuments { get; set; }
    public string ErrorMessage { get; set; } = "";

    public sealed class FolderNodeVM
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string? Name { get; set; } = "";
        public string? Path { get; set; }
        public int Level { get; set; } // pra indent no MVP
        public Guid UploadFolderId { get; set; }
        public Guid ListingFolderId { get; set; }
        public bool IsVirtual { get; set; }
        public bool CanReceiveDocuments { get; set; } = true;
    }

    public sealed class DocumentRowVM
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? TypeName { get; set; }
        public string? FileName { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public string UploadedAtLocalFormatted { get; set; } = "";
        public Guid CreatedBy { get; set; }   // ou Guid?
        public string? OcrStatus { get; set; }
        public DateTime? OcrFinishedAt { get; set; }
        public bool HasOcrText { get; set; }
        public bool IsOcrAvailable { get; set; }
        public string OcrBadgeText { get; set; } = "Sem OCR";
        public string OcrBadgeCss { get; set; } = "bg-secondary";
        public int PartialPartsWithOcrCount { get; set; }
        public int PartialPartsWithoutOcrCount { get; set; }
        public bool HasAnyPartialOcr { get; set; }
        public bool HasAllPartialOcr { get; set; }
        public string OcrSummaryText { get; set; } = "";
        public string OcrSummaryCss { get; set; } = "";
        public Guid? ClassificationId { get; set; }
        public string? ClassificationLabel { get; set; }
        public string? ClassificationColor { get; set; }
        public string? ClassificationIcon { get; set; }
        public bool HasOcrClassificationSuggestion { get; set; }
        public Guid? SuggestedClassificationId { get; set; }
        public string? SuggestedClassificationLabel { get; set; }
        public bool IsPartialDocument { get; set; }
        public bool IsDocumentIncomplete { get; set; }
        public int? PartNumber { get; set; }
        public int? TotalParts { get; set; }
        public Guid? ConsolidatedVersionId { get; set; }
        public string PartialStatus { get; set; } = "NOT_PARTIAL";
        public Guid? PartialGroupId { get; set; }
        public int? PartialPartNumber { get; set; }
        public int? PartialTotalParts { get; set; }
        public int PartialPartsCount { get; set; }
        public string PartialStatusLabel { get; set; } = "";
        public string PartialStatusCss { get; set; } = "";

        public bool IsConfidential { get; set; }
        public int? QualityScore { get; set; }
        public string? QualityStatus { get; set; }
        public string? QualityNextAction { get; set; }
    }
}
