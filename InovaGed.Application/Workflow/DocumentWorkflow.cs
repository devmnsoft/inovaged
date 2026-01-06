using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Workflow;

public interface IDocumentWorkflowQueries
{
    Task<DocumentWorkflowStateDto?> GetCurrentAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    Task<IReadOnlyList<DocumentWorkflowTransitionDto>> ListAvailableTransitionsAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken ct);

    Task<IReadOnlyList<DocumentWorkflowHistoryDto>> ListHistoryAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken ct);

    Task<IReadOnlyList<WorkflowLogRow>> ListAsync(Guid tenantId, Guid documentId, int take, CancellationToken ct);


}
public interface IDocumentWorkflowCommands
{
    Task<Result<Guid>> StartAsync(Guid tenantId, Guid documentId, Guid workflowId, Guid? userId, CancellationToken ct);
    Task<Result> ApplyTransitionAsync(
         Guid tenantId,
         Guid documentWorkflowId,
         Guid transitionId,
         string? reason,
         string? comments,
         Guid? userId,
         CancellationToken ct);
}

public sealed record WorkflowLogRow(
  DateTimeOffset CreatedAt,
  string FromStatus,
  string ToStatus,
  string? Reason,
  Guid? CreatedBy,
  string? UserName);

public sealed record DocumentWorkflowStateDto(
    Guid DocumentWorkflowId,
    Guid WorkflowId,
    string WorkflowName,
    Guid CurrentStageId,
    string CurrentStageName,
    bool IsCompleted,
    DateTime StartedAt,
    Guid? StartedBy
);

public sealed record DocumentWorkflowTransitionDto(
    Guid Id,
    string Name,
    Guid ToStageId,
    string ToStageName,
    bool RequiresReason
);

public sealed record DocumentWorkflowHistoryDto(
    long Id,
    string? FromStageName,
    string ToStageName,
    DateTime PerformedAt,
    Guid? PerformedBy,
    string? Reason,
    string? Comments
);
