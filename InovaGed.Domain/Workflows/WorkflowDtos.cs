namespace InovaGed.Domain.Workflow;

public sealed class WorkflowDefinitionRowDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class WorkflowDefinitionDetailsDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; }
}

public sealed class WorkflowStageRowDto
{
    public Guid Id { get; init; }
    public Guid WorkflowId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
    public bool IsStart { get; init; }
    public bool IsFinal { get; init; }
    public string? RequiredRole { get; init; }
}

public sealed class WorkflowTransitionRowDto
{
    public Guid Id { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid FromStageId { get; init; }
    public Guid ToStageId { get; init; }
    public string Name { get; init; } = "";
    public bool RequiresReason { get; init; }
}
public sealed class DocumentWorkflowCurrentDto
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid CurrentStageId { get; init; }
    public string CurrentStageName { get; init; } = "";
    public bool IsFinal { get; init; }
}
public sealed class DocumentWorkflowDto
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid CurrentStageId { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

public sealed class DocumentWorkflowHistoryRowDto
{
    public long Id { get; init; }
    public Guid DocumentWorkflowId { get; init; }
    public Guid? FromStageId { get; init; }
    public Guid ToStageId { get; init; }
    public Guid? PerformedBy { get; init; }
    public DateTime PerformedAt { get; init; }
    public string? Reason { get; init; }
    public string? Comments { get; init; }
}

public sealed class CreateWorkflowDefinitionCommand
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
}

public sealed class UpdateWorkflowDefinitionCommand
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class CreateWorkflowStageCommand
{
    public Guid WorkflowId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
    public bool IsStart { get; init; }
    public bool IsFinal { get; init; }
    public string? RequiredRole { get; init; }
}

public sealed class UpdateWorkflowStageCommand
{
    public Guid Id { get; init; }
    public Guid WorkflowId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
    public bool IsStart { get; init; }
    public bool IsFinal { get; init; }
    public string? RequiredRole { get; init; }
}

public sealed class CreateWorkflowTransitionCommand
{
    public Guid WorkflowId { get; init; }
    public Guid FromStageId { get; init; }
    public Guid ToStageId { get; init; }
    public string Name { get; init; } = "";
    public bool RequiresReason { get; init; }
}

public sealed class UpdateWorkflowTransitionCommand
{
    public Guid Id { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid FromStageId { get; init; }
    public Guid ToStageId { get; init; }
    public string Name { get; init; } = "";
    public bool RequiresReason { get; init; }
}
