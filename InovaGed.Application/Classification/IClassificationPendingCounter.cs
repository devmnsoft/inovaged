namespace InovaGed.Application.Classification;

public interface IClassificationPendingCounter
{
    Task<int> CountPendingAsync(Guid tenantId, CancellationToken ct);
}
