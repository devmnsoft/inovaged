namespace InovaGed.Application.HospitalBilling;

public sealed record HospitalBillingFilter(string? Insurer = null, string? Competence = null, string? Status = null, bool? HasDenial = null);
public sealed record HospitalBillingKpis(int Total, int Pending, int Approved, int Divergent, int WithDenial, decimal Presented, decimal ApprovedAmount, decimal Denied, decimal InAppeal, decimal Recovered, int WithoutOcr, int LowConfidence);
public sealed class HospitalBillingDocumentDto
{
    public Guid Id { get; init; } public Guid DocumentId { get; init; } public string Title { get; init; } = "Documento";
    public string DocumentType { get; init; } = "Conta hospitalar"; public string? Insurer { get; init; } public string? Provider { get; init; }
    public string? GuideNumber { get; init; } public string? AuthorizationNumber { get; init; } public string? Competence { get; init; }
    public string MaskedPatient { get; init; } = "Dado protegido"; public decimal PresentedAmount { get; init; } public decimal ApprovedAmount { get; init; }
    public decimal DeniedAmount { get; init; } public decimal RecoveredAmount { get; init; } public decimal Confidence { get; init; }
    public string Status { get; init; } = "PENDING_REVIEW"; public string? DenialReason { get; init; } public DateOnly? DueDate { get; init; }
}
public sealed record HospitalBillingDashboard(HospitalBillingKpis Kpis, IReadOnlyList<HospitalBillingDocumentDto> Documents);
public interface IHospitalBillingQueries
{
    Task<HospitalBillingDashboard> DashboardAsync(Guid tenantId, HospitalBillingFilter filter, CancellationToken ct);
    Task<HospitalBillingDocumentDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);
}
