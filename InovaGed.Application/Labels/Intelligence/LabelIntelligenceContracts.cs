namespace InovaGed.Application.Labels.Intelligence;

public static class LabelAlertType { public const string PrintedNeverScanned="PRINTED_NEVER_SCANNED",LocationDivergence="LOCATION_DIVERGENCE",BoxWithoutLabel="BOX_WITHOUT_LABEL",DocumentWithoutLabel="DOCUMENT_WITHOUT_LABEL",ReplacedLabelScanned="REPLACED_LABEL_SCANNED",DuplicateScan="DUPLICATE_SCAN",InventoryDivergence="INVENTORY_DIVERGENCE",ReprintWithoutRecentScan="REPRINT_WITHOUT_RECENT_SCAN"; }
public static class LabelAlertStatus { public const string Open="OPEN",InProgress="IN_PROGRESS",Resolved="RESOLVED",Ignored="IGNORED"; }
public static class LabelAlertSeverity { public const string Low="LOW",Medium="MEDIUM",High="HIGH",Critical="CRITICAL"; }

public sealed record LabelIntelligenceFilter(DateTime? From=null,DateTime? To=null,string? Location=null,string? SubjectType=null);
public sealed record LabelMetric(string Label,int Value);
public sealed record LabelIntelligenceDashboard(int Printed,int Scanned,int NeverScanned,int Divergences,int Replacements,int BoxesWithoutLabel,int DocumentsWithoutLabel,int OpenAlerts,IReadOnlyList<LabelMetric> ScansByDay,IReadOnlyList<LabelMetric> DivergencesByLocation,IReadOnlyList<LabelMetric> Templates,IReadOnlyList<LabelMetric> PrintsByUser,IReadOnlyList<LabelMetric> AlertsBySeverity);
public sealed record LabelDivergenceItem(Guid Id,string SubjectType,Guid? SubjectId,string? ControlNumber,string? ExpectedLocation,string? FoundLocation,DateTime DetectedAt,string Source);
public sealed record LabelNeverScannedItem(Guid PrintHistoryId,string SubjectType,Guid SubjectId,string? ControlNumber,string TemplateCode,string? Location,DateTime PrintedAt,Guid PrintedBy);
public sealed record LabelWithoutPrintItem(Guid SubjectId,string SubjectType,string ControlNumber,string? Location,DateTime? CreatedAt);
public interface ILabelIntelligenceService { Task<LabelIntelligenceDashboard> GetDashboardAsync(Guid tenantId,LabelIntelligenceFilter filter,CancellationToken ct); Task<IReadOnlyList<LabelDivergenceItem>> ListDivergencesAsync(Guid tenantId,LabelIntelligenceFilter filter,CancellationToken ct); Task<IReadOnlyList<LabelNeverScannedItem>> ListNeverScannedAsync(Guid tenantId,LabelIntelligenceFilter filter,CancellationToken ct); Task<IReadOnlyList<LabelWithoutPrintItem>> ListObjectsWithoutLabelAsync(Guid tenantId,string subjectType,CancellationToken ct); }

public sealed record LabelAlertFilter(string? Status=null,string? Severity=null,string? Type=null);
public sealed record LabelAlertItem(Guid Id,string AlertType,string Severity,string? SubjectType,Guid? SubjectId,string? ControlNumber,string? Location,string Title,string Message,string Status,DateTime DetectedAt,string? ResolutionNotes);
public interface ILabelAlertService { Task<int> DetectAlertsAsync(Guid tenantId,CancellationToken ct); Task<IReadOnlyList<LabelAlertItem>> ListAsync(Guid tenantId,LabelAlertFilter filter,CancellationToken ct); Task ResolveAsync(Guid tenantId,Guid alertId,Guid userId,string notes,CancellationToken ct); Task IgnoreAsync(Guid tenantId,Guid alertId,Guid userId,string notes,CancellationToken ct); }

public sealed record LabelCustodyEventCommand(Guid TenantId,string SubjectType,Guid? SubjectId,string? ControlNumber,string EventType,string EventTitle,string? EventDescription,string? SourceTable,Guid? SourceId,string? LocationFrom,string? LocationTo,Guid? PerformedBy,string? IpAddress,string? UserAgent,string? PayloadJson=null);
public sealed record LabelCustodyEventItem(Guid Id,string EventType,string EventTitle,string? EventDescription,string? SourceTable,Guid? SourceId,string? LocationFrom,string? LocationTo,Guid? PerformedBy,DateTime PerformedAt,string? IpAddress,string? UserAgent);
public sealed record LabelCustodyTimeline(string SubjectType,Guid? SubjectId,string? ControlNumber,IReadOnlyList<LabelCustodyEventItem> Events);
public interface ILabelCustodyService { Task RegisterEventAsync(LabelCustodyEventCommand command,CancellationToken ct); Task<LabelCustodyTimeline> GetTimelineAsync(Guid tenantId,string subjectType,Guid subjectId,CancellationToken ct); Task<LabelCustodyTimeline> GetTimelineByControlAsync(Guid tenantId,string controlNumber,CancellationToken ct); }
