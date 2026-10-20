namespace InovaGed.Application.Labels.Tracking;

public static class LabelScanStatus { public const string Valid="VALID",InvalidPayload="INVALID_PAYLOAD",NotFound="NOT_FOUND",TenantMismatch="TENANT_MISMATCH",Replaced="REPLACED",LocationDivergence="LOCATION_DIVERGENCE"; }
public static class LabelInventoryStatus { public const string Open="OPEN",Closed="CLOSED",Cancelled="CANCELLED"; }
public static class LabelInventoryItemStatus { public const string Found="FOUND",DivergentLocation="DIVERGENT_LOCATION",Unknown="UNKNOWN",Duplicated="DUPLICATED"; }
public static class LabelReplacementStatus { public const string Requested="REQUESTED",Approved="APPROVED",Completed="COMPLETED",Rejected="REJECTED"; }

public sealed record LabelScanCommand(Guid TenantId,Guid UserId,string Payload,string? Location,string Source,string? IpAddress,string? UserAgent);
public sealed record LabelScanResult(Guid EventId,string Status,string Message,LabelTraceDetails? Trace);
public sealed class LabelTraceDetails
{
    public LabelTraceDetails() { }
    public LabelTraceDetails(Guid? PrintHistoryId, string SubjectType, Guid? SubjectId, string? ControlNumber, string? TemplateCode, string? PrintMode, string? ExpectedLocation, string? CurrentLocation, DateTime? PrintedAt, DateTime? LastScanAt, string Status)
    {
        this.PrintHistoryId = PrintHistoryId;
        this.SubjectType = SubjectType;
        this.SubjectId = SubjectId;
        this.ControlNumber = ControlNumber;
        this.TemplateCode = TemplateCode;
        this.PrintMode = PrintMode;
        this.ExpectedLocation = ExpectedLocation;
        this.CurrentLocation = CurrentLocation;
        this.PrintedAt = PrintedAt;
        this.LastScanAt = LastScanAt;
        this.Status = Status;
    }
    public Guid? PrintHistoryId { get; set; }
    public string SubjectType { get; set; } = "";
    public Guid? SubjectId { get; set; }
    public string? ControlNumber { get; set; }
    public string? TemplateCode { get; set; }
    public string? PrintMode { get; set; }
    public string? ExpectedLocation { get; set; }
    public string? CurrentLocation { get; set; }
    public DateTime? PrintedAt { get; set; }
    public DateTime? LastScanAt { get; set; }
    public string Status { get; set; } = "";
}
public sealed record LabelScanEventFilter(DateTime? From=null,DateTime? To=null,string? Status=null,string? ControlNumber=null);
public sealed class LabelScanEventItem
{
    public LabelScanEventItem() { }
    public LabelScanEventItem(Guid Id, string SubjectType, string? ControlNumber, string? LocationScanned, string Status, string? Message, DateTime ScannedAt)
    {
        this.Id = Id;
        this.SubjectType = SubjectType;
        this.ControlNumber = ControlNumber;
        this.LocationScanned = LocationScanned;
        this.Status = Status;
        this.Message = Message;
        this.ScannedAt = ScannedAt;
    }
    public Guid Id { get; set; }
    public string SubjectType { get; set; } = "";
    public string? ControlNumber { get; set; }
    public string? LocationScanned { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public DateTime ScannedAt { get; set; }
}
public interface ILabelTrackingService { Task<LabelScanResult> ScanAsync(LabelScanCommand command,CancellationToken ct); Task<LabelTraceDetails?> TraceAsync(Guid tenantId,string payloadOrCode,CancellationToken ct); Task<IReadOnlyList<LabelScanEventItem>> ListEventsAsync(Guid tenantId,LabelScanEventFilter filter,CancellationToken ct); }

public sealed record LabelInventoryStartCommand(Guid TenantId,Guid UserId,string Title,string? ExpectedLocation,string? Notes);
public sealed record LabelInventoryScanCommand(Guid TenantId,Guid SessionId,Guid UserId,string Payload,string? FoundLocation,string? IpAddress,string? UserAgent);
public sealed record LabelInventoryScanResult(Guid ItemId,string Status,string Message);
public sealed class LabelInventoryItem
{
    public LabelInventoryItem() { }
    public LabelInventoryItem(Guid Id, string SubjectType, string? ControlNumber, string? ExpectedLocation, string? FoundLocation, string Status, string? DivergenceMessage, DateTime ScannedAt)
    {
        this.Id = Id;
        this.SubjectType = SubjectType;
        this.ControlNumber = ControlNumber;
        this.ExpectedLocation = ExpectedLocation;
        this.FoundLocation = FoundLocation;
        this.Status = Status;
        this.DivergenceMessage = DivergenceMessage;
        this.ScannedAt = ScannedAt;
    }
    public Guid Id { get; set; }
    public string SubjectType { get; set; } = "";
    public string? ControlNumber { get; set; }
    public string? ExpectedLocation { get; set; }
    public string? FoundLocation { get; set; }
    public string Status { get; set; } = "";
    public string? DivergenceMessage { get; set; }
    public DateTime ScannedAt { get; set; }
}
public sealed record LabelInventoryDetails(Guid Id,string SessionNumber,string Title,string? ExpectedLocation,string Status,DateTime StartedAt,string? Notes,IReadOnlyList<LabelInventoryItem> Items);
public interface ILabelInventoryService { Task<Guid> StartSessionAsync(LabelInventoryStartCommand command,CancellationToken ct); Task<LabelInventoryDetails?> GetSessionAsync(Guid tenantId,Guid sessionId,CancellationToken ct); Task<LabelInventoryScanResult> AddScanAsync(LabelInventoryScanCommand command,CancellationToken ct); Task CloseSessionAsync(Guid tenantId,Guid sessionId,Guid userId,string? notes,CancellationToken ct); Task CancelSessionAsync(Guid tenantId,Guid sessionId,Guid userId,string reason,CancellationToken ct); }

public sealed record LabelReplacementRequestCommand(Guid TenantId,Guid UserId,Guid? OldPrintHistoryId,string SubjectType,Guid? SubjectId,string? ControlNumber,string Reason,string? OldTemplateCode);
public sealed class LabelReplacementItem
{
    public LabelReplacementItem() { }
    public LabelReplacementItem(Guid Id, string SubjectType, string? ControlNumber, string Reason, string Status, DateTime RequestedAt)
    {
        this.Id = Id;
        this.SubjectType = SubjectType;
        this.ControlNumber = ControlNumber;
        this.Reason = Reason;
        this.Status = Status;
        this.RequestedAt = RequestedAt;
    }
    public Guid Id { get; set; }
    public string SubjectType { get; set; } = "";
    public string? ControlNumber { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime RequestedAt { get; set; }
}
public interface ILabelReplacementService { Task<Guid> RequestReplacementAsync(LabelReplacementRequestCommand command,CancellationToken ct); Task ApproveAsync(Guid tenantId,Guid requestId,Guid userId,CancellationToken ct); Task CompleteAsync(Guid tenantId,Guid requestId,Guid newPrintHistoryId,Guid userId,CancellationToken ct); Task RejectAsync(Guid tenantId,Guid requestId,Guid userId,string reason,CancellationToken ct); Task<IReadOnlyList<LabelReplacementItem>> ListAsync(Guid tenantId,CancellationToken ct); }
