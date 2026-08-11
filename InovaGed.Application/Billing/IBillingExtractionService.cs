namespace InovaGed.Application.Billing;
public interface IBillingExtractionService
{
    bool LooksFinancial(string text);
    BillingExtractionDto Extract(BillingExtractionCandidate candidate);
    Task<BillingExtractionDto> ExtractAsync(Guid tenantId, BillingExtractionCandidate candidate, CancellationToken ct);
    Task<BillingExtractionDto?> ExtractDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
