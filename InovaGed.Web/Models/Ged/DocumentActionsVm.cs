using System;

namespace InovaGed.Web.Models.Ged;

public sealed class DocumentActionsVm
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public Guid? CurrentFolderId { get; set; }
    public bool IsOcrAvailable { get; set; }
    public bool IsPartialDocument { get; set; }
    public bool IsDocumentIncomplete { get; set; }
    public string PartialStatus { get; set; } = "NOT_PARTIAL";
    public bool CanDelete { get; set; } = true;
    public bool CanMove { get; set; } = true;
    public bool CanClassify { get; set; } = true;
}
