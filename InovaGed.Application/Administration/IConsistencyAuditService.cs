namespace InovaGed.Application.Administration;

public sealed record ConsistencyIssue(string Code, string Title, string Description, int Count, string ModuleUrl, bool Available);
public sealed record ConsistencyAuditResult(IReadOnlyList<ConsistencyIssue> Issues, DateTimeOffset CheckedAt);

public interface IConsistencyAuditService
{
    Task<ConsistencyAuditResult> CheckAsync(Guid tenantId, CancellationToken ct);
}
