using InovaGed.Domain.Primitives;
using InovaGed.Domain.Workflow;

namespace InovaGed.Application.Workflow;

public interface IWorkflowQueries
{
    Task<IReadOnlyList<WorkflowDefinitionRowDto>> ListDefinitionsAsync(Guid tenantId, string? q, CancellationToken ct);
    Task<WorkflowDefinitionDetailsDto?> GetDefinitionAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<IReadOnlyList<WorkflowStageRowDto>> ListStagesAsync(Guid tenantId, Guid workflowId, CancellationToken ct);
    Task<IReadOnlyList<WorkflowTransitionRowDto>> ListTransitionsAsync(Guid tenantId, Guid workflowId, CancellationToken ct);

    Task<IReadOnlyList<WorkflowTransitionRowDto>> ListAvailableTransitionsAsync(Guid tenantId, Guid workflowId, Guid currentStageId, CancellationToken ct);
}

public interface IWorkflowCommands
{
    Task<Result<Guid>> CreateDefinitionAsync(Guid tenantId, CreateWorkflowDefinitionCommand cmd, Guid? userId, CancellationToken ct);
    Task<Result> UpdateDefinitionAsync(Guid tenantId, UpdateWorkflowDefinitionCommand cmd, Guid? userId, CancellationToken ct);
    Task<Result> DeactivateDefinitionAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct);

    Task<Result<Guid>> CreateStageAsync(Guid tenantId, CreateWorkflowStageCommand cmd, Guid? userId, CancellationToken ct);
    Task<Result> UpdateStageAsync(Guid tenantId, UpdateWorkflowStageCommand cmd, Guid? userId, CancellationToken ct);
    Task<Result> DeleteStageAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<Result<Guid>> CreateTransitionAsync(Guid tenantId, CreateWorkflowTransitionCommand cmd, Guid? userId, CancellationToken ct);
    Task<Result> UpdateTransitionAsync(Guid tenantId, UpdateWorkflowTransitionCommand cmd, Guid? userId, CancellationToken ct);
    Task<Result> DeleteTransitionAsync(Guid tenantId, Guid id, CancellationToken ct);

}
