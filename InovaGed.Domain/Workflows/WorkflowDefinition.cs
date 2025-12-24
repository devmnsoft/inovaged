using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Workflows;

public sealed class WorkflowDefinition : TenantEntity
{
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private WorkflowDefinition() { }

    public WorkflowDefinition(Guid tenantId, string name, Guid createdBy)
    {
        TenantId = tenantId;
        Name = name.Trim();
        CreatedBy = createdBy;
    }
}
