namespace InovaGed.Application.PhysicalArchive.Reconciliation;

public static class ArchiveReconciliationIssueType
{
 public const string BoxWithoutDocuments="BOX_WITHOUT_DOCUMENTS",DocumentWithoutBox="DOCUMENT_WITHOUT_BOX",BoxWithoutLabel="BOX_WITHOUT_LABEL",DocumentWithoutLabel="DOCUMENT_WITHOUT_LABEL",LabelWithoutSubject="LABEL_WITHOUT_SUBJECT",MultipleActiveLabels="MULTIPLE_ACTIVE_LABELS",ReplacedLabelInUse="REPLACED_LABEL_IN_USE",LocationMismatch="LOCATION_MISMATCH",MissingRequiredLabelField="MISSING_REQUIRED_LABEL_FIELD",ClassificationMismatch="CLASSIFICATION_MISMATCH",RetentionMismatch="RETENTION_MISMATCH",MixedBoxRequiresReview="MIXED_BOX_REQUIRES_REVIEW",InventoryMissingExpectedItem="INVENTORY_MISSING_EXPECTED_ITEM",InventoryUnexpectedItem="INVENTORY_UNEXPECTED_ITEM";
}
public static class ArchiveReconciliationIssueStatus { public const string Open="OPEN",InReview="IN_REVIEW",Fixed="FIXED",Ignored="IGNORED",Rejected="REJECTED"; }
public static class ReconciliationSeverity { public const string Critical="CRITICAL",High="HIGH",Medium="MEDIUM",Low="LOW"; }
public sealed record ArchiveReconciliationStartCommand(Guid TenantId,Guid UserId,string Source="MANUAL",Guid? InventorySessionId=null);
public sealed record ArchiveReconciliationRunFilter(string? Status=null,string? Source=null);
public sealed record ArchiveReconciliationIssueFilter(string? Status=null,string? Severity=null,string? IssueType=null,Guid? RunId=null,string? Search=null);
public sealed record ArchiveReconciliationRunItem(Guid Id,string RunNumber,string Source,string Status,int TotalChecked,int TotalIssues,int TotalCritical,int TotalHigh,int TotalMedium,int TotalLow,DateTime StartedAt,DateTime? FinishedAt);
public sealed record ArchiveReconciliationIssueItem(Guid Id,Guid RunId,string IssueType,string Severity,string? SubjectType,Guid? SubjectId,string? ControlNumber,string Title,string Description,string? Suggestion,string? ProposedAction,string Status,DateTime CreatedAt);
public sealed record ArchiveReconciliationRunDetails(ArchiveReconciliationRunItem Run,IReadOnlyList<ArchiveReconciliationIssueItem> Issues);
public sealed record ArchiveReconciliationIssueDetails(ArchiveReconciliationIssueItem Issue,string? ExpectedValue,string? FoundValue,string? ProposedPayload,string? ResolutionNotes,DateTime? ResolvedAt);
public sealed record ArchiveReconciliationResult(Guid RunId,int TotalChecked,int TotalIssues,int Critical,int High,int Medium,int Low);
public sealed record ArchiveReconciliationDashboard(int Boxes,int Documents,int Labels,int OpenIssues,int FixedIssues,int CriticalIssues,int IgnoredIssues,int LocationMismatches,int WithoutLabel,int OrphanLabels,decimal Quality,IReadOnlyList<ArchiveReconciliationRunItem> RecentRuns);
public interface IArchiveReconciliationService
{
 Task<Guid> StartRunAsync(ArchiveReconciliationStartCommand command,CancellationToken ct);
 Task<ArchiveReconciliationDashboard> GetDashboardAsync(Guid tenantId,CancellationToken ct);
 Task<ArchiveReconciliationRunDetails?> GetRunAsync(Guid tenantId,Guid runId,CancellationToken ct);
 Task<IReadOnlyList<ArchiveReconciliationRunItem>> ListRunsAsync(Guid tenantId,ArchiveReconciliationRunFilter filter,CancellationToken ct);
 Task<IReadOnlyList<ArchiveReconciliationIssueItem>> ListIssuesAsync(Guid tenantId,ArchiveReconciliationIssueFilter filter,CancellationToken ct);
 Task<ArchiveReconciliationIssueDetails?> GetIssueAsync(Guid tenantId,Guid issueId,CancellationToken ct);
}
public interface IArchiveReconciliationEngine { Task<ArchiveReconciliationResult> RunFullDiagnosisAsync(Guid tenantId,Guid userId,CancellationToken ct); Task<ArchiveReconciliationResult> RunInventoryReconciliationAsync(Guid tenantId,Guid inventorySessionId,Guid userId,CancellationToken ct); }
public interface IArchiveReconciliationFixService { Task ApplyFixAsync(Guid tenantId,Guid issueId,Guid userId,string? notes,CancellationToken ct); Task IgnoreIssueAsync(Guid tenantId,Guid issueId,Guid userId,string reason,CancellationToken ct); Task RejectSuggestionAsync(Guid tenantId,Guid issueId,Guid userId,string reason,CancellationToken ct); }
