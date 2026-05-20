namespace InovaGed.Application.Audit;

public interface IAuditSecurityService
{
    Task<AuditDashboardVM> GetDashboardAsync(Guid tenantId, CancellationToken ct);
    Task RegisterCriticalActionAsync(Guid tenantId, Guid? userId, string action, string entityName, string entityId, string summary, string? ipAddress, CancellationToken ct);
}
