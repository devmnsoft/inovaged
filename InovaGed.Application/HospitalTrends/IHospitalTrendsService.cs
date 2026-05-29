namespace InovaGed.Application.HospitalTrends;

public interface IHospitalTrendsService
{
    Task<HospitalTrendsDashboardDto> GetDashboardAsync(HospitalTrendsFilter filter, CancellationToken ct);
    Task<IReadOnlyList<TrendKpiDto>> GetTermTrendsAsync(HospitalTrendsFilter filter, CancellationToken ct);
    Task<IReadOnlyList<HospitalAlertDto>> GetAlertsAsync(HospitalTrendsFilter filter, CancellationToken ct);
    Task<IReadOnlyList<SectorTrendDto>> GetSectorTrendsAsync(HospitalTrendsFilter filter, CancellationToken ct);
    Task<IReadOnlyList<OperationalTrendDto>> GetOperationalTrendsAsync(HospitalTrendsFilter filter, CancellationToken ct);
}
