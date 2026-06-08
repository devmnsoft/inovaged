namespace InovaGed.Web.Models.Ged;

public sealed class DocumentSidePanelVm
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string TypeName { get; set; } = "Sem classificação";
    public string FolderName { get; set; } = "";
    public string ClassificationName { get; set; } = "Sem classificação";
    public int VersionNumber { get; set; }
    public string CreatedAtLocalFormatted { get; set; } = "";
    public string UploadedAtLocalFormatted { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public string SizeBytesFormatted { get; set; } = "";
    public string Extension { get; set; } = "";
    public string DocumentStatus { get; set; } = "";
    public string Visibility { get; set; } = "";
    public string OcrStatus { get; set; } = "NONE";
    public bool IsOcrAvailable { get; set; }
    public string OcrBadgeText { get; set; } = "OCR indisponível";
    public string OcrBadgeCss { get; set; } = "bg-secondary";
    public bool IsPartialDocument { get; set; }
    public bool IsDocumentIncomplete { get; set; }
    public string PartialStatus { get; set; } = "NOT_PARTIAL";
    public string PartialStatusLabel { get; set; } = "";
    public int PartialPartsCount { get; set; }
    public int? PartialTotalParts { get; set; }
    public string PreviewUrl { get; set; } = "";
    public string OcrTextUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string DetailsUrl { get; set; } = "";
    public string PartsUrl { get; set; } = "";
    public string HistoryUrl { get; set; } = "";
    public bool CanMove { get; set; }
    public bool CanClassify { get; set; }
    public bool CanAddPart { get; set; }
    public bool CanViewParts { get; set; }
    public bool CanConsolidate { get; set; }
    public bool CanRunOcr { get; set; }
    public bool CanReprocessOcr { get; set; }
    public bool CanCancelPartial { get; set; }
    public bool CanDelete { get; set; }
    public IReadOnlyList<DocumentSidePanelPartVm> Parts { get; set; } = Array.Empty<DocumentSidePanelPartVm>();
    public IReadOnlyList<DocumentSidePanelHistoryVm> History { get; set; } = Array.Empty<DocumentSidePanelHistoryVm>();
}

public sealed class DocumentSidePanelPartVm
{
    public Guid VersionId { get; set; }
    public int PartNumber { get; set; }
    public int? TotalParts { get; set; }
    public string FileName { get; set; } = "";
    public string UploadedAtLocalFormatted { get; set; } = "";
    public string UploadedByName { get; set; } = "";
    public string Status { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

public sealed class DocumentSidePanelHistoryVm
{
    public string OccurredAtLocalFormatted { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string? CorrelationId { get; set; }
}
