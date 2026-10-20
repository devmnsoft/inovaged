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
public sealed class ArchiveReconciliationRunItem
{
    public ArchiveReconciliationRunItem() { }
    public ArchiveReconciliationRunItem(Guid Id, string RunNumber, string Source, string Status, int TotalChecked, int TotalIssues, int TotalCritical, int TotalHigh, int TotalMedium, int TotalLow, DateTime StartedAt, DateTime? FinishedAt)
    {
        this.Id = Id;
        this.RunNumber = RunNumber;
        this.Source = Source;
        this.Status = Status;
        this.TotalChecked = TotalChecked;
        this.TotalIssues = TotalIssues;
        this.TotalCritical = TotalCritical;
        this.TotalHigh = TotalHigh;
        this.TotalMedium = TotalMedium;
        this.TotalLow = TotalLow;
        this.StartedAt = StartedAt;
        this.FinishedAt = FinishedAt;
    }
    public Guid Id { get; set; }
    public string RunNumber { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public int TotalChecked { get; set; }
    public int TotalIssues { get; set; }
    public int TotalCritical { get; set; }
    public int TotalHigh { get; set; }
    public int TotalMedium { get; set; }
    public int TotalLow { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
public sealed class ArchiveReconciliationIssueItem
{
    public ArchiveReconciliationIssueItem() { }
    public ArchiveReconciliationIssueItem(Guid Id, Guid RunId, string IssueType, string Severity, string? SubjectType, Guid? SubjectId, string? ControlNumber, string Title, string Description, string? Suggestion, string? ProposedAction, string Status, DateTime CreatedAt)
    {
        this.Id = Id;
        this.RunId = RunId;
        this.IssueType = IssueType;
        this.Severity = Severity;
        this.SubjectType = SubjectType;
        this.SubjectId = SubjectId;
        this.ControlNumber = ControlNumber;
        this.Title = Title;
        this.Description = Description;
        this.Suggestion = Suggestion;
        this.ProposedAction = ProposedAction;
        this.Status = Status;
        this.CreatedAt = CreatedAt;
    }
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string IssueType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string? ControlNumber { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Suggestion { get; set; }
    public string? ProposedAction { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
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
