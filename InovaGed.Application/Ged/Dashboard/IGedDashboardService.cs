namespace InovaGed.Application.Ged.Dashboard;

public interface IGedDashboardService
{
    Task<GedDashboardVm> GetAsync(Guid tenantId, Guid userId, CancellationToken ct);
    Task<GedDashboardVm> GetAsync(Guid tenantId, Guid userId, int periodDays, CancellationToken ct);
}
