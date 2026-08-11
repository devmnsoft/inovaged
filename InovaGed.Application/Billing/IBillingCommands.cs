namespace InovaGed.Application.Billing;
public interface IBillingCommands
{
    Task SaveExtractionAsync(Guid tenantId, BillingExtractionDto extraction, CancellationToken ct);
    Task<bool> ReviewAsync(Guid tenantId, Guid extractionId, Guid userId, BillingReviewInput input, CancellationToken ct);
}
