namespace InovaGed.Application.Labels.Intelligence;

public static class LabelAlertType { public const string PrintedNeverScanned="PRINTED_NEVER_SCANNED",LocationDivergence="LOCATION_DIVERGENCE",BoxWithoutLabel="BOX_WITHOUT_LABEL",DocumentWithoutLabel="DOCUMENT_WITHOUT_LABEL",ReplacedLabelScanned="REPLACED_LABEL_SCANNED",DuplicateScan="DUPLICATE_SCAN",InventoryDivergence="INVENTORY_DIVERGENCE",ReprintWithoutRecentScan="REPRINT_WITHOUT_RECENT_SCAN"; }
public static class LabelAlertStatus { public const string Open="OPEN",InProgress="IN_PROGRESS",Resolved="RESOLVED",Ignored="IGNORED"; }
public static class LabelAlertSeverity { public const string Low="LOW",Medium="MEDIUM",High="HIGH",Critical="CRITICAL"; }

public sealed record LabelIntelligenceFilter(DateTime? From=null,DateTime? To=null,string? Location=null,string? SubjectType=null);
public sealed record LabelMetric(string Label,int Value);
public sealed record LabelIntelligenceDashboard(int Printed,int Scanned,int NeverScanned,int Divergences,int Replacements,int BoxesWithoutLabel,int DocumentsWithoutLabel,int OpenAlerts,IReadOnlyList<LabelMetric> ScansByDay,IReadOnlyList<LabelMetric> DivergencesByLocation,IReadOnlyList<LabelMetric> Templates,IReadOnlyList<LabelMetric> PrintsByUser,IReadOnlyList<LabelMetric> AlertsBySeverity);
public sealed class LabelDivergenceItem
{
    public LabelDivergenceItem() { }
    public LabelDivergenceItem(Guid Id, string SubjectType, Guid? SubjectId, string? ControlNumber, string? ExpectedLocation, string? FoundLocation, DateTime DetectedAt, string Source)
    {
        this.Id = Id;
        this.SubjectType = SubjectType;
        this.SubjectId = SubjectId;
        this.ControlNumber = ControlNumber;
        this.ExpectedLocation = ExpectedLocation;
        this.FoundLocation = FoundLocation;
        this.DetectedAt = DetectedAt;
        this.Source = Source;
    }
    public Guid Id { get; set; }
    public string SubjectType { get; set; } = "";
    public Guid? SubjectId { get; set; }
    public string? ControlNumber { get; set; }
    public string? ExpectedLocation { get; set; }
    public string? FoundLocation { get; set; }
    public DateTime DetectedAt { get; set; }
    public string Source { get; set; } = "";
}
public sealed class LabelNeverScannedItem
{
    public LabelNeverScannedItem() { }
    public LabelNeverScannedItem(Guid PrintHistoryId, string SubjectType, Guid SubjectId, string? ControlNumber, string TemplateCode, string? Location, DateTime PrintedAt, Guid PrintedBy)
    {
        this.PrintHistoryId = PrintHistoryId;
        this.SubjectType = SubjectType;
        this.SubjectId = SubjectId;
        this.ControlNumber = ControlNumber;
        this.TemplateCode = TemplateCode;
        this.Location = Location;
        this.PrintedAt = PrintedAt;
        this.PrintedBy = PrintedBy;
    }
    public Guid PrintHistoryId { get; set; }
    public string SubjectType { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string? ControlNumber { get; set; }
    public string TemplateCode { get; set; } = "";
    public string? Location { get; set; }
    public DateTime PrintedAt { get; set; }
    public Guid PrintedBy { get; set; }
}
public sealed class LabelWithoutPrintItem
{
    public LabelWithoutPrintItem() { }
    public LabelWithoutPrintItem(Guid SubjectId, string SubjectType, string ControlNumber, string? Location, DateTime? CreatedAt)
    {
        this.SubjectId = SubjectId;
        this.SubjectType = SubjectType;
        this.ControlNumber = ControlNumber;
        this.Location = Location;
        this.CreatedAt = CreatedAt;
    }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = "";
    public string ControlNumber { get; set; } = "";
    public string? Location { get; set; }
    public DateTime? CreatedAt { get; set; }
}
public interface ILabelIntelligenceService { Task<LabelIntelligenceDashboard> GetDashboardAsync(Guid tenantId,LabelIntelligenceFilter filter,CancellationToken ct); Task<IReadOnlyList<LabelDivergenceItem>> ListDivergencesAsync(Guid tenantId,LabelIntelligenceFilter filter,CancellationToken ct); Task<IReadOnlyList<LabelNeverScannedItem>> ListNeverScannedAsync(Guid tenantId,LabelIntelligenceFilter filter,CancellationToken ct); Task<IReadOnlyList<LabelWithoutPrintItem>> ListObjectsWithoutLabelAsync(Guid tenantId,string subjectType,CancellationToken ct); }

public sealed record LabelAlertFilter(string? Status=null,string? Severity=null,string? Type=null);
public sealed class LabelAlertItem
{
    public LabelAlertItem() { }
    public LabelAlertItem(Guid Id, string AlertType, string Severity, string? SubjectType, Guid? SubjectId, string? ControlNumber, string? Location, string Title, string Message, string Status, DateTime DetectedAt, string? ResolutionNotes)
    {
        this.Id = Id;
        this.AlertType = AlertType;
        this.Severity = Severity;
        this.SubjectType = SubjectType;
        this.SubjectId = SubjectId;
        this.ControlNumber = ControlNumber;
        this.Location = Location;
        this.Title = Title;
        this.Message = Message;
        this.Status = Status;
        this.DetectedAt = DetectedAt;
        this.ResolutionNotes = ResolutionNotes;
    }
    public Guid Id { get; set; }
    public string AlertType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string? ControlNumber { get; set; }
    public string? Location { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime DetectedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}
public interface ILabelAlertService { Task<int> DetectAlertsAsync(Guid tenantId,CancellationToken ct); Task<IReadOnlyList<LabelAlertItem>> ListAsync(Guid tenantId,LabelAlertFilter filter,CancellationToken ct); Task ResolveAsync(Guid tenantId,Guid alertId,Guid userId,string notes,CancellationToken ct); Task IgnoreAsync(Guid tenantId,Guid alertId,Guid userId,string notes,CancellationToken ct); }

public sealed record LabelCustodyEventCommand(Guid TenantId,string SubjectType,Guid? SubjectId,string? ControlNumber,string EventType,string EventTitle,string? EventDescription,string? SourceTable,Guid? SourceId,string? LocationFrom,string? LocationTo,Guid? PerformedBy,string? IpAddress,string? UserAgent,string? PayloadJson=null);
public sealed class LabelCustodyEventItem
{
    public LabelCustodyEventItem() { }
    public LabelCustodyEventItem(Guid Id, string EventType, string EventTitle, string? EventDescription, string? SourceTable, Guid? SourceId, string? LocationFrom, string? LocationTo, Guid? PerformedBy, DateTime PerformedAt, string? IpAddress, string? UserAgent)
    {
        this.Id = Id;
        this.EventType = EventType;
        this.EventTitle = EventTitle;
        this.EventDescription = EventDescription;
        this.SourceTable = SourceTable;
        this.SourceId = SourceId;
        this.LocationFrom = LocationFrom;
        this.LocationTo = LocationTo;
        this.PerformedBy = PerformedBy;
        this.PerformedAt = PerformedAt;
        this.IpAddress = IpAddress;
        this.UserAgent = UserAgent;
    }
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string EventTitle { get; set; } = "";
    public string? EventDescription { get; set; }
    public string? SourceTable { get; set; }
    public Guid? SourceId { get; set; }
    public string? LocationFrom { get; set; }
    public string? LocationTo { get; set; }
    public Guid? PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
public sealed record LabelCustodyTimeline(string SubjectType,Guid? SubjectId,string? ControlNumber,IReadOnlyList<LabelCustodyEventItem> Events);
public interface ILabelCustodyService { Task RegisterEventAsync(LabelCustodyEventCommand command,CancellationToken ct); Task<LabelCustodyTimeline> GetTimelineAsync(Guid tenantId,string subjectType,Guid subjectId,CancellationToken ct); Task<LabelCustodyTimeline> GetTimelineByControlAsync(Guid tenantId,string controlNumber,CancellationToken ct); }
