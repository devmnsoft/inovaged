namespace InovaGed.Application.Classification;

public sealed record ClassificationPlanDashboard(int Classes, int ClassesWithoutRule, int RulesInReview, int PublishedVersion, int PendingChanges, int PermanentDestinations);
public sealed record ClassificationTreeNode(Guid Id, Guid? ParentId, string Code, string Title, string? ActivityType, string ReviewStatus, bool IsActive, bool HasRetentionRule);
public sealed record ClassificationNodeDetails(Guid Id, Guid? ParentId, string Code, string Title, string? Description, string? ActivityType, string? DocumentFunction, string? NormativeSource, string? Keywords, int DisplayOrder, string ReviewStatus, bool IsActive);
public sealed record ClassificationNodeCreateCommand(Guid TenantId, Guid UserId, Guid? ParentId, string Code, string Title, string? Description, string? ActivityType, string? DocumentFunction, string? NormativeSource, string? Keywords, int DisplayOrder, string ReviewStatus, bool IsActive);
public sealed record ClassificationNodeUpdateCommand(Guid TenantId, Guid UserId, Guid Id, Guid? ParentId, string Code, string Title, string? Description, string? ActivityType, string? DocumentFunction, string? NormativeSource, string? Keywords, int DisplayOrder, string ReviewStatus, bool IsActive);

public interface IClassificationPlanService
{
    Task<ClassificationPlanDashboard> GetDashboardAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<ClassificationTreeNode>> GetTreeAsync(Guid tenantId, CancellationToken ct);
    Task<ClassificationNodeDetails?> GetNodeAsync(Guid tenantId, Guid nodeId, CancellationToken ct);
    Task<Guid> CreateNodeAsync(ClassificationNodeCreateCommand command, CancellationToken ct);
    Task UpdateNodeAsync(ClassificationNodeUpdateCommand command, CancellationToken ct);
}

public sealed record RetentionRuleDetails(Guid Id, Guid ClassificationNodeId, string ClassificationCode, string ClassificationTitle, int? CurrentPhaseYears, int? IntermediatePhaseYears, string FinalDestination, string? TriggerEvent, string? TriggerDescription, string? LegalBasis, string? Observation, string ReviewStatus, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);
public sealed record RetentionRuleSaveCommand(Guid TenantId, Guid UserId, Guid? Id, Guid ClassificationNodeId, int? CurrentPhaseYears, int? IntermediatePhaseYears, string FinalDestination, string? TriggerEvent, string? TriggerDescription, string? LegalBasis, string? Observation, string ReviewStatus, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);
public sealed record RetentionRuleListItem(Guid Id, Guid ClassificationNodeId, string Code, string Title, int? CurrentPhaseYears, int? IntermediatePhaseYears, string FinalDestination, string? TriggerEvent, string? LegalBasis, string ReviewStatus);
public sealed record RetentionRuleFilter(string? Search = null, string? Status = null);
public interface IRetentionRuleV2Service
{
    Task<RetentionRuleDetails?> GetByClassificationAsync(Guid tenantId, Guid classificationNodeId, CancellationToken ct);
    Task SaveAsync(RetentionRuleSaveCommand command, CancellationToken ct);
    Task<IReadOnlyList<RetentionRuleListItem>> ListAsync(Guid tenantId, RetentionRuleFilter filter, CancellationToken ct);
}

public sealed record ClassificationVersionItem(Guid Id, int VersionNumber, string Title, string Status, DateTimeOffset? PublishedAt, Guid? PublishedBy, string? Notes, int ClassCount, int RuleCount);
public sealed record ClassificationVersionDifference(string Kind, string Entity, string Code, string Description);
public sealed record ClassificationVersionCompareResult(Guid FromVersionId, Guid ToVersionId, IReadOnlyList<ClassificationVersionDifference> Differences);
public interface IClassificationVersionService
{
    Task<IReadOnlyList<ClassificationVersionItem>> ListVersionsAsync(Guid tenantId, CancellationToken ct);
    Task<Guid> PublishAsync(Guid tenantId, Guid userId, string notes, CancellationToken ct);
    Task<ClassificationVersionCompareResult> CompareAsync(Guid tenantId, Guid fromVersionId, Guid toVersionId, CancellationToken ct);
}
