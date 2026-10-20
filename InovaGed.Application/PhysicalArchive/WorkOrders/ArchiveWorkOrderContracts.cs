namespace InovaGed.Application.PhysicalArchive.WorkOrders;

public static class ArchiveWorkOrderType { public const string PrintLabel="PRINT_LABEL",ReplaceLabel="REPLACE_LABEL",CheckLocation="CHECK_LOCATION",MoveBox="MOVE_BOX",ReconcileIssue="RECONCILE_ISSUE",InventoryDivergence="INVENTORY_DIVERGENCE",BoxConference="BOX_CONFERENCE",DocumentConference="DOCUMENT_CONFERENCE",LoanDelivery="LOAN_DELIVERY",LoanReturn="LOAN_RETURN"; }
public static class ArchiveWorkOrderStatus { public const string Open="OPEN",Assigned="ASSIGNED",InProgress="IN_PROGRESS",WaitingValidation="WAITING_VALIDATION",Completed="COMPLETED",Closed="CLOSED",Cancelled="CANCELLED"; }
public static class ArchiveWorkOrderPriority { public const string Low="LOW",Medium="MEDIUM",High="HIGH",Critical="CRITICAL"; }
public sealed record ArchiveWorkOrderCreateCommand(Guid TenantId,Guid UserId,string Title,string OrderType,string Priority=ArchiveWorkOrderPriority.Medium,string? Description=null,string? SubjectType=null,Guid? SubjectId=null,Guid? BoxId=null,Guid? DocumentId=null,string? ControlNumber=null,string? LocationExpected=null,string? LocationFound=null,string? LocationTarget=null,string? SourceType=null,Guid? SourceId=null,Guid? AssignedTo=null,DateTimeOffset? DueAt=null);
public sealed record ArchiveWorkOrderFilter(string? Status=null,string? OrderType=null,string? Priority=null,Guid? AssignedTo=null,string? Location=null,string? ControlNumber=null,string? SourceType=null,string? Due=null);
public sealed class ArchiveWorkOrderListItem
{
    public ArchiveWorkOrderListItem() { }
    public ArchiveWorkOrderListItem(Guid Id, string OrderNumber, string Title, string OrderType, string Priority, string Status, Guid? AssignedTo, string? AssignedName, DateTimeOffset? DueAt, string? SourceType, string? LocationTarget, string? ControlNumber, DateTimeOffset CreatedAt)
    {
        this.Id = Id;
        this.OrderNumber = OrderNumber;
        this.Title = Title;
        this.OrderType = OrderType;
        this.Priority = Priority;
        this.Status = Status;
        this.AssignedTo = AssignedTo;
        this.AssignedName = AssignedName;
        this.DueAt = DueAt;
        this.SourceType = SourceType;
        this.LocationTarget = LocationTarget;
        this.ControlNumber = ControlNumber;
        this.CreatedAt = CreatedAt;
    }
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string OrderType { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid? AssignedTo { get; set; }
    public string? AssignedName { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string? SourceType { get; set; }
    public string? LocationTarget { get; set; }
    public string? ControlNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ArchiveWorkOrderTaskItem
{
    public ArchiveWorkOrderTaskItem() { }
    public ArchiveWorkOrderTaskItem(Guid Id, string Title, string? Description, string TaskType, string Status, int DisplayOrder, bool RequiresScan, bool RequiresEvidence, Guid? AssignedTo, DateTimeOffset? CompletedAt, string? ResultNotes)
    {
        this.Id = Id;
        this.Title = Title;
        this.Description = Description;
        this.TaskType = TaskType;
        this.Status = Status;
        this.DisplayOrder = DisplayOrder;
        this.RequiresScan = RequiresScan;
        this.RequiresEvidence = RequiresEvidence;
        this.AssignedTo = AssignedTo;
        this.CompletedAt = CompletedAt;
        this.ResultNotes = ResultNotes;
    }
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string TaskType { get; set; } = "";
    public string Status { get; set; } = "";
    public int DisplayOrder { get; set; }
    public bool RequiresScan { get; set; }
    public bool RequiresEvidence { get; set; }
    public Guid? AssignedTo { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultNotes { get; set; }
}
public sealed class ArchiveWorkOrderEventItem
{
    public ArchiveWorkOrderEventItem() { }
    public ArchiveWorkOrderEventItem(Guid Id, Guid? TaskId, string EventType, string EventTitle, string? EventDescription, string? FromStatus, string? ToStatus, Guid? PerformedBy, DateTimeOffset PerformedAt)
    {
        this.Id = Id;
        this.TaskId = TaskId;
        this.EventType = EventType;
        this.EventTitle = EventTitle;
        this.EventDescription = EventDescription;
        this.FromStatus = FromStatus;
        this.ToStatus = ToStatus;
        this.PerformedBy = PerformedBy;
        this.PerformedAt = PerformedAt;
    }
    public Guid Id { get; set; }
    public Guid? TaskId { get; set; }
    public string EventType { get; set; } = "";
    public string EventTitle { get; set; } = "";
    public string? EventDescription { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public Guid? PerformedBy { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
}
public sealed class ArchiveWorkOrderEvidenceItem
{
    public ArchiveWorkOrderEvidenceItem() { }
    public ArchiveWorkOrderEvidenceItem(Guid Id, Guid? TaskId, Guid? LabelEvidenceId, string? Description, Guid? AttachedBy, DateTimeOffset AttachedAt)
    {
        this.Id = Id;
        this.TaskId = TaskId;
        this.LabelEvidenceId = LabelEvidenceId;
        this.Description = Description;
        this.AttachedBy = AttachedBy;
        this.AttachedAt = AttachedAt;
    }
    public Guid Id { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? LabelEvidenceId { get; set; }
    public string? Description { get; set; }
    public Guid? AttachedBy { get; set; }
    public DateTimeOffset AttachedAt { get; set; }
}
public sealed record ArchiveWorkOrderDetails(ArchiveWorkOrderListItem Order,string? Description,string? SubjectType,Guid? SubjectId,Guid? BoxId,Guid? DocumentId,string? LocationExpected,string? LocationFound,DateTimeOffset? StartedAt,DateTimeOffset? CompletedAt,DateTimeOffset? ClosedAt,string? CloseNotes,IReadOnlyList<ArchiveWorkOrderTaskItem> Tasks,IReadOnlyList<ArchiveWorkOrderEventItem> Events,IReadOnlyList<ArchiveWorkOrderEvidenceItem> Evidences);
public sealed record ArchiveWorkOrderTaskResult(string Notes,bool ScanConfirmed=false,Guid? LabelEvidenceId=null,string? EvidenceDescription=null);
public sealed class ArchiveWorkOrderSlaSummary
{
    public ArchiveWorkOrderSlaSummary() { }
    public ArchiveWorkOrderSlaSummary(int Open, int InProgress, int Overdue, int Critical, int WaitingValidation, int CompletedThisMonth, double AverageCompletionHours, int ActiveOperators)
    {
        this.Open = Open;
        this.InProgress = InProgress;
        this.Overdue = Overdue;
        this.Critical = Critical;
        this.WaitingValidation = WaitingValidation;
        this.CompletedThisMonth = CompletedThisMonth;
        this.AverageCompletionHours = AverageCompletionHours;
        this.ActiveOperators = ActiveOperators;
    }
    public int Open { get; set; }
    public int InProgress { get; set; }
    public int Overdue { get; set; }
    public int Critical { get; set; }
    public int WaitingValidation { get; set; }
    public int CompletedThisMonth { get; set; }
    public double AverageCompletionHours { get; set; }
    public int ActiveOperators { get; set; }
}
public sealed class ArchiveWorkOrderSlaItem
{
    public ArchiveWorkOrderSlaItem() { }
    public ArchiveWorkOrderSlaItem(Guid Id, string OrderNumber, string Title, string Priority, Guid? AssignedTo, string? AssignedName, DateTimeOffset DueAt, double OverdueHours)
    {
        this.Id = Id;
        this.OrderNumber = OrderNumber;
        this.Title = Title;
        this.Priority = Priority;
        this.AssignedTo = AssignedTo;
        this.AssignedName = AssignedName;
        this.DueAt = DueAt;
        this.OverdueHours = OverdueHours;
    }
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Priority { get; set; } = "";
    public Guid? AssignedTo { get; set; }
    public string? AssignedName { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public double OverdueHours { get; set; }
}
public interface IArchiveWorkOrderService { Task<Guid>CreateAsync(ArchiveWorkOrderCreateCommand command,CancellationToken ct); Task<Guid>CreateFromAlertAsync(Guid tenantId,Guid alertId,Guid userId,CancellationToken ct); Task<Guid>CreateFromReconciliationIssueAsync(Guid tenantId,Guid issueId,Guid userId,CancellationToken ct); Task<ArchiveWorkOrderDetails?>GetAsync(Guid tenantId,Guid orderId,CancellationToken ct); Task<IReadOnlyList<ArchiveWorkOrderListItem>>ListAsync(Guid tenantId,ArchiveWorkOrderFilter filter,CancellationToken ct); Task AssignAsync(Guid tenantId,Guid orderId,Guid assignedTo,Guid assignedBy,DateTimeOffset? dueAt,CancellationToken ct); Task StartAsync(Guid tenantId,Guid orderId,Guid userId,CancellationToken ct); Task CompleteAsync(Guid tenantId,Guid orderId,Guid userId,string notes,CancellationToken ct); Task CloseAsync(Guid tenantId,Guid orderId,Guid userId,string notes,CancellationToken ct); Task CancelAsync(Guid tenantId,Guid orderId,Guid userId,string reason,CancellationToken ct); }
public interface IArchiveWorkOrderTaskService { Task CompleteTaskAsync(Guid tenantId,Guid taskId,Guid userId,ArchiveWorkOrderTaskResult result,CancellationToken ct); Task ReopenTaskAsync(Guid tenantId,Guid taskId,Guid userId,string reason,CancellationToken ct); }
public interface IArchiveWorkOrderSlaService { Task<ArchiveWorkOrderSlaSummary>GetSummaryAsync(Guid tenantId,CancellationToken ct); Task<IReadOnlyList<ArchiveWorkOrderSlaItem>>ListOverdueAsync(Guid tenantId,CancellationToken ct); }
