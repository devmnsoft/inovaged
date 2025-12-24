using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Workflows;

public sealed class WorkflowTransition : TenantEntity
{
    public Guid WorkflowDefinitionId { get; private set; }
    public Guid FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }

    public string? RequiredPermission { get; private set; } // ex: DOC_WORKFLOW
    public bool RequireComment { get; private set; }

    private WorkflowTransition() { }

    public WorkflowTransition(Guid tenantId, Guid workflowId, Guid fromStageId, Guid toStageId,
        string? requiredPermission, bool requireComment, Guid createdBy)
    {
        TenantId = tenantId;
        WorkflowDefinitionId = workflowId;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        RequiredPermission = requiredPermission;
        RequireComment = requireComment;
        CreatedBy = createdBy;
    }
}
