namespace InovaGed.Application.RetentionCases;

public interface IRetentionCaseExecutionRepository
{
    Task<ExecuteCaseResult> ExecuteCaseAsync(Guid tenantId, Guid userId, Guid caseId, CancellationToken ct);
}

public sealed record ExecuteCaseResult(int ExecutedItems, int BlockedItems);