namespace InovaGed.Application.Billing;

public interface IBillingRuleService
{
    Task<IReadOnlyList<BillingExtractionRuleDto>> ListAsync(Guid tenantId, CancellationToken ct);
    Task<Guid> SaveAsync(Guid tenantId, Guid userId, BillingExtractionRuleInput input, CancellationToken ct);
    Task<bool> DeleteAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
}
