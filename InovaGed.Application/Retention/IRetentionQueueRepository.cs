namespace InovaGed.Application.Retention
{
    public interface IRetentionQueueRepository
    {
        Task<IReadOnlyList<RetentionRuleRow>> ListRulesAsync(Guid tenantId, CancellationToken ct);
        Task<Guid> UpsertRuleAsync(Guid tenantId, Guid? id, RetentionRuleRow rule, CancellationToken ct);

        Task<int> GenerateQueueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct);

        Task<IReadOnlyList<RetentionQueueRow>> ListQueueAsync(Guid tenantId, string bucket, CancellationToken ct);
        Task MarkInTermAsync(Guid tenantId, IEnumerable<Guid> queueIds, CancellationToken ct);
        Task MarkDoneForDocumentsAsync(Guid tenantId, IEnumerable<Guid> documentIds, CancellationToken ct);
    }
}
