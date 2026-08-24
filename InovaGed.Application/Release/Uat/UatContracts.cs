namespace InovaGed.Application.Release.Uat;

public interface IUatTestPlanService
{
    Task<UatTestPlanDetails> GetDefaultPlanAsync(Guid? tenantId, CancellationToken ct);
    Task<IReadOnlyList<UatTestPlanItem>> ListPlansAsync(Guid? tenantId, CancellationToken ct);
    Task<Guid> CreatePlanAsync(UatTestPlanCreateCommand command, CancellationToken ct);
}
public interface IUatExecutionService
{
    Task<Guid> StartRunAsync(Guid? tenantId, Guid planId, Guid userId, CancellationToken ct);
    Task RecordResultAsync(UatTestResultCommand command, CancellationToken ct);
    Task<UatRunDetails> GetRunAsync(Guid runId, CancellationToken ct);
    Task<UatSummary> GetSummaryAsync(Guid? tenantId, CancellationToken ct);
}
public interface IReleaseEvidenceService
{
    Task<Guid> AddEvidenceAsync(ReleaseEvidenceCommand command, CancellationToken ct);
    Task<IReadOnlyList<ReleaseEvidenceItem>> ListEvidenceAsync(string sourceType, Guid sourceId, CancellationToken ct);
}
public sealed record UatTestPlanCreateCommand(Guid? TenantId,string PlanCode,string Title,string? Description,string? ReleaseVersion,Guid? CreatedBy);
public sealed record UatTestResultCommand(Guid? TenantId,Guid RunId,Guid TestCaseId,string Result,string? ActualResult,string? EvidenceNotes,Guid? IncidentId,Guid ExecutedBy);
public sealed record ReleaseEvidenceCommand(Guid? TenantId,string SourceType,Guid SourceId,string Title,string? Description,string EvidenceType,string? FilePath,string? ExternalUrl,string? PayloadJson,Guid CapturedBy);
public sealed record UatTestPlanItem(Guid Id,string PlanCode,string Title,string Status,string? ReleaseVersion,DateTimeOffset CreatedAt,int CaseCount);
public sealed record UatTestCaseItem(Guid Id,string ModuleCode,string CaseCode,string Title,string? Description,string? Preconditions,string Steps,string ExpectedResult,string Priority,string Status,int DisplayOrder);
public sealed record UatTestPlanDetails(UatTestPlanItem Plan,IReadOnlyList<UatTestCaseItem> Cases);
public sealed record UatResultItem(Guid Id,Guid TestCaseId,string CaseCode,string ModuleCode,string Title,string Priority,string Result,string? ActualResult,string? EvidenceNotes,Guid? IncidentId,string? IncidentStatus,DateTimeOffset ExecutedAt);
public sealed record UatRunDetails(Guid Id,Guid PlanId,string PlanCode,string PlanTitle,string? ReleaseVersion,Guid? StartedBy,DateTimeOffset StartedAt,DateTimeOffset? FinishedAt,string Status,int TotalCases,int PassedCases,int FailedCases,int BlockedCases,IReadOnlyList<UatResultItem> Results);
public sealed record UatSummary(int TotalCases,int PassedCases,int FailedCases,int BlockedCases,int PendingCases,int IncidentCount,int EvidenceCount,string Status,Guid? LatestRunId,string? CurrentPlan,decimal PassedPercentage);
public sealed record ReleaseEvidenceItem(Guid Id,string SourceType,Guid? SourceId,string Title,string? Description,string EvidenceType,string? FilePath,string? ExternalUrl,string? PayloadJson,DateTimeOffset CapturedAt);
