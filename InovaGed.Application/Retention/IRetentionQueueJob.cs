namespace InovaGed.Application.Retention
{
    public interface IRetentionQueueJob
    {
        Task<int> RunAsync(Guid tenantId, Guid? userId, string? userName, CancellationToken ct);
    }
}
