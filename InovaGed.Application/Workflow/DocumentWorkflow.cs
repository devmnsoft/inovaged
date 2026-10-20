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

public sealed class WorkflowLogRow
{
    public WorkflowLogRow() { }
    public WorkflowLogRow(DateTimeOffset CreatedAt, string FromStatus, string ToStatus, string? Reason, Guid? CreatedBy, string? UserName)
    {
        this.CreatedAt = CreatedAt;
        this.FromStatus = FromStatus;
        this.ToStatus = ToStatus;
        this.Reason = Reason;
        this.CreatedBy = CreatedBy;
        this.UserName = UserName;
    }
    public DateTimeOffset CreatedAt { get; set; }
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public string? Reason { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? UserName { get; set; }
}

public sealed class DocumentWorkflowStateDto
{
    public DocumentWorkflowStateDto() { }
    public DocumentWorkflowStateDto(Guid DocumentWorkflowId, Guid WorkflowId, string WorkflowName, Guid CurrentStageId, string CurrentStageName, bool IsCompleted, DateTime StartedAt, Guid? StartedBy)
    {
        this.DocumentWorkflowId = DocumentWorkflowId;
        this.WorkflowId = WorkflowId;
        this.WorkflowName = WorkflowName;
        this.CurrentStageId = CurrentStageId;
        this.CurrentStageName = CurrentStageName;
        this.IsCompleted = IsCompleted;
        this.StartedAt = StartedAt;
        this.StartedBy = StartedBy;
    }
    public Guid DocumentWorkflowId { get; set; }
    public Guid WorkflowId { get; set; }
    public string WorkflowName { get; set; } = "";
    public Guid CurrentStageId { get; set; }
    public string CurrentStageName { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime StartedAt { get; set; }
    public Guid? StartedBy { get; set; }
}

public sealed class DocumentWorkflowTransitionDto
{
    public DocumentWorkflowTransitionDto() { }
    public DocumentWorkflowTransitionDto(Guid Id, string Name, Guid ToStageId, string ToStageName, bool RequiresReason)
    {
        this.Id = Id;
        this.Name = Name;
        this.ToStageId = ToStageId;
        this.ToStageName = ToStageName;
        this.RequiresReason = RequiresReason;
    }
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid ToStageId { get; set; }
    public string ToStageName { get; set; } = "";
    public bool RequiresReason { get; set; }
}

public sealed class DocumentWorkflowHistoryDto
{
    public DocumentWorkflowHistoryDto() { }
    public DocumentWorkflowHistoryDto(long Id, string? FromStageName, string ToStageName, DateTime PerformedAt, Guid? PerformedBy, string? Reason, string? Comments)
    {
        this.Id = Id;
        this.FromStageName = FromStageName;
        this.ToStageName = ToStageName;
        this.PerformedAt = PerformedAt;
        this.PerformedBy = PerformedBy;
        this.Reason = Reason;
        this.Comments = Comments;
    }
    public long Id { get; set; }
    public string? FromStageName { get; set; }
    public string ToStageName { get; set; } = "";
    public DateTime PerformedAt { get; set; }
    public Guid? PerformedBy { get; set; }
    public string? Reason { get; set; }
    public string? Comments { get; set; }
}
