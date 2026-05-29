namespace InovaGed.Application.HospitalIntelligence;

public interface IHospitalIntelligenceService
{
    Task<HospitalIntelligenceDashboardDto> GetDashboardAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<OcrKpiDto>> GetOcrKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<ClinicalTermKpiDto>> GetClinicalTermKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<FinancialDocumentKpiDto>> GetFinancialKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<OperationalKpiDto>> GetOperationalKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<RiskAlertKpiDto>> GetRiskAlertsAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
}
