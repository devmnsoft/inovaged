using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Workflows;

public sealed class DocumentWorkflowHistory : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public Guid WorkflowDefinitionId { get; private set; }
    public Guid FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }
    public string? Comment { get; private set; }

    private DocumentWorkflowHistory() { }

    public DocumentWorkflowHistory(Guid tenantId, Guid documentId, Guid workflowId, Guid fromStageId, Guid toStageId,
        string? comment, Guid createdBy)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        WorkflowDefinitionId = workflowId;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        Comment = comment;
        CreatedBy = createdBy;
    }
}
