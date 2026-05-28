namespace InovaGed.Application.DemoReadiness;

public interface IDemoReadinessService
{
    Task<DemoReadinessReportDto> RunAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
