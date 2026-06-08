using System;

namespace InovaGed.Web.Models.Ged;

public sealed class DocumentActionsVm
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? VersionId { get; set; }
    public Guid? CurrentFolderId { get; set; }

    public bool IsOcrAvailable { get; set; }
    public bool IsPartialDocument { get; set; }
    public bool IsDocumentIncomplete { get; set; }
    public string PartialStatus { get; set; } = "NOT_PARTIAL";
    public int PartialPartsCount { get; set; }
    public int? PartialTotalParts { get; set; }

    public bool CanMove { get; set; } = true;
    public bool CanClassify { get; set; } = true;
    public bool CanDelete { get; set; } = true;
    public bool CanAddPart { get; set; } = true;
    public bool CanViewParts { get; set; } = true;
    public bool CanConsolidate { get; set; } = true;
    public bool CanCancelPartial { get; set; } = false;
    public bool CanMarkAsIncomplete { get; set; } = true;

    public bool IsCompletePartial => string.Equals(PartialStatus, "COMPLETE", StringComparison.OrdinalIgnoreCase) || (PartialPartsCount > 1 && (!PartialTotalParts.HasValue || PartialPartsCount >= PartialTotalParts.Value));
    public bool IsConsolidated => string.Equals(PartialStatus, "CONSOLIDATED", StringComparison.OrdinalIgnoreCase);
    public bool ShouldShowPartialActions => IsDocumentIncomplete || IsPartialDocument;
}
