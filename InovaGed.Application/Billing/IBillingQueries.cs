namespace InovaGed.Application.Billing;
public interface IBillingQueries
{
    Task<(BillingKpis Kpis, IReadOnlyList<BillingExtractionDto> Rows)> DashboardAsync(Guid tenantId, BillingFilter filter, CancellationToken ct);
    Task<BillingExtractionDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);
}
