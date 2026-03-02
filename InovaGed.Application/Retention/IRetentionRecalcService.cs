namespace InovaGed.Application.Retention;

public interface IRetentionRecalcService
{
    Task<int> RunAsync(Guid tenantId, int dueSoonDays, CancellationToken ct);
    Task<int> RunOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct);
}