using InovaGed.Domain.Primitives;
using InovaGed.Domain.Workflow;

namespace InovaGed.Application.Workflow;
 
public interface IDocumentWorkflowQueries
{
    Task<DocumentWorkflowCurrentDto?> GetCurrentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<IReadOnlyList<DocumentWorkflowHistoryRowDto>> ListHistoryAsync(Guid tenantId, Guid documentWorkflowId, CancellationToken ct);
}

public interface IDocumentWorkflowCommands
{
    Task<Result<Guid>> StartAsync(Guid tenantId, Guid documentId, Guid workflowId, Guid? userId, CancellationToken ct);
    Task<Result> ApplyTransitionAsync(Guid tenantId, Guid documentWorkflowId, Guid transitionId, string? reason, string? comments, Guid? userId, CancellationToken ct);
   
}
