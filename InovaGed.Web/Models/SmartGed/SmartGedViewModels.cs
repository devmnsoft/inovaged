using Microsoft.AspNetCore.Mvc.Rendering;
using InovaGed.Application.SmartGed;

namespace InovaGed.Web.Models.SmartGed;

public sealed class SmartGedSearchPageVM
{
    public string? Query { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? ClassificationId { get; set; }
    public Guid? TypeId { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public string? RetentionStatus { get; set; }
    public SmartGedSearchResult Result { get; set; } = new(string.Empty, [], 0);
    public IReadOnlyList<SelectListItem> Folders { get; set; } = [];
    public IReadOnlyList<SelectListItem> Classifications { get; set; } = [];
    public IReadOnlyList<SelectListItem> DocumentTypes { get; set; } = [];
}

public sealed class SmartGedDocumentVM
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public string? CurrentClassification { get; set; }
    public Guid? CurrentClassificationId { get; set; }
    public string? CurrentRetentionStatus { get; set; }
    public string? OcrStatus { get; set; }
    public string? OcrText { get; set; }
    public bool OcrPending { get; set; }
    public DocumentIntelligenceDetails? Intelligence { get; set; }
    public bool CanClassify { get; set; }
    public bool CanEdit { get; set; }
}
