using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Workflows;

public sealed class WorkflowStage : TenantEntity
{
    public Guid WorkflowDefinitionId { get; private set; }
    public string Name { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsInitial { get; private set; }
    public bool IsFinal { get; private set; }

    private WorkflowStage() { }

    public WorkflowStage(Guid tenantId, Guid workflowId, string name, int sortOrder, bool isInitial, bool isFinal, Guid createdBy)
    {
        TenantId = tenantId;
        WorkflowDefinitionId = workflowId;
        Name = name.Trim();
        SortOrder = sortOrder;
        IsInitial = isInitial;
        IsFinal = isFinal;
        CreatedBy = createdBy;
    }
}
