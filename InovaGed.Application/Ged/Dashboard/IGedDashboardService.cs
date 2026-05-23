namespace InovaGed.Application.Ged.Dashboard;

public interface IGedDashboardService
{
    Task<GedDashboardVm> GetAsync(Guid tenantId, Guid userId, bool forceRefresh, CancellationToken ct);
}
