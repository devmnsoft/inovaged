namespace InovaGed.Application.HospitalIntelligence;

public interface IHospitalIntelligenceService
{
    Task<HospitalIntelligenceDashboardDto> GetDashboardAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<TermOccurrenceDto>> GetClinicalTermsAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<FinancialSignalDto>> GetFinancialSignalsAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<OperationalSignalDto>> GetOperationalSignalsAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<DocumentAlertDto>> GetCriticalAlertsAsync(HospitalIntelligenceFilter filter, CancellationToken ct);
}
