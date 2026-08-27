namespace InovaGed.Application.Governance;

public static class GovernanceAlertType
{
    public const string DocumentWithoutOcr = "DOCUMENT_WITHOUT_OCR";
    public const string DocumentWithoutClassification = "DOCUMENT_WITHOUT_CLASSIFICATION";
    public const string SensitiveDataDetected = "SENSITIVE_DATA_DETECTED";
    public const string RetentionPending = "RETENTION_PENDING";
    public const string BoxWithoutLocation = "BOX_WITHOUT_LOCATION";
    public const string OverdueLoan = "OVERDUE_LOAN";
    public const string WorkflowOverdue = "WORKFLOW_OVERDUE";
    public const string CriticalIncidentOpen = "CRITICAL_INCIDENT_OPEN";
    public const string LabelReprintWithoutReason = "LABEL_REPRINT_WITHOUT_REASON";
    public const string SchemaPending = "SCHEMA_PENDING";
}

public sealed record GovernanceDashboard(int DocumentsWithoutOcr, int DocumentsWithoutClassification, int SensitiveDocuments, int RetentionPending, int BoxesWithoutLocation, int OverdueLoans, int OverdueTasks, int CriticalIncidents, int OpenAlerts, int ExportsThisMonth, bool SchemaReady);
public sealed record GovernanceAuditFilter(Guid TenantId, DateTimeOffset? From = null, DateTimeOffset? To = null, string? User = null, string? EventType = null, string? Module = null, Guid? DocumentId = null, Guid? BoxId = null, string? Ip = null, string? CorrelationId = null, string? Search = null);
public sealed record GovernanceAuditItem(DateTimeOffset OccurredAt, string EventType, string Module, string UserName, string Origin, string Description, string? Ip, string? CorrelationId, Guid? SourceId);
public sealed record GovernanceAlertFilter(Guid TenantId, string? Status = null, string? Severity = null, string? Type = null, string? SourceType = null, Guid? AssignedTo = null, DateTimeOffset? From = null, DateTimeOffset? To = null);
public sealed record GovernanceAlertItem(Guid Id, string AlertType, string Severity, string Title, string? Description, string? SourceType, Guid? SourceId, string? RecommendedAction, string Status, DateTimeOffset CreatedAt);
public sealed record GovernanceAlertCreateCommand(Guid TenantId, string AlertType, string Severity, string Title, string? Description, string? SourceType, Guid? SourceId, string? RecommendedAction, Guid? UserId);
public sealed record GovernanceEvidenceCreateCommand(Guid TenantId, string SourceType, Guid? SourceId, string Title, string? Description, string? PayloadJson, Guid? UserId, string? UserName);
public sealed record GovernanceEvidenceItem(Guid Id, string EvidenceCode, string SourceType, Guid? SourceId, string Title, string? Description, string? EvidenceHash, string? RegisteredByName, DateTimeOffset RegisteredAt);
public sealed record GovernanceReportQuery(Guid TenantId, string ReportType, DateTimeOffset? From = null, DateTimeOffset? To = null, Guid? UserId = null, string? UserName = null);
public sealed record GovernanceReportRow(string Reference, string Description, string Status, DateTimeOffset? Date);
public sealed record GovernanceReportResult(string ReportType, string Title, IReadOnlyList<GovernanceReportRow> Rows, bool SchemaReady);

public interface IGovernanceDashboardService { Task<GovernanceDashboard> GetDashboardAsync(Guid tenantId, Guid userId, CancellationToken ct); }
public interface IGovernanceAuditService { Task<IReadOnlyList<GovernanceAuditItem>> ListAsync(GovernanceAuditFilter filter, CancellationToken ct); }
public interface IGovernanceAlertService { Task<IReadOnlyList<GovernanceAlertItem>> ListAsync(GovernanceAlertFilter filter, CancellationToken ct); Task<Guid> CreateAsync(GovernanceAlertCreateCommand command, CancellationToken ct); Task ResolveAsync(Guid tenantId, Guid alertId, Guid userId, string notes, CancellationToken ct); }
public interface IGovernanceEvidenceService { Task<Guid> RegisterAsync(GovernanceEvidenceCreateCommand command, CancellationToken ct); Task<IReadOnlyList<GovernanceEvidenceItem>> ListBySourceAsync(Guid tenantId, string? sourceType, Guid? sourceId, CancellationToken ct); }
public interface IGovernanceReportService { Task<GovernanceReportResult> GenerateAsync(GovernanceReportQuery query, CancellationToken ct); Task<byte[]> ExportCsvAsync(GovernanceReportQuery query, CancellationToken ct); }
