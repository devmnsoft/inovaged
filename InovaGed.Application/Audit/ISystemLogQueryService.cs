namespace InovaGed.Application.Audit;

public interface ISystemLogQueryService
{
    Task<PagedResult<SystemLogListItemDto>> SearchAsync(SystemLogFilter filter, CancellationToken ct);
    Task<SystemLogDetailsDto?> GetDetailsAsync(string id, Guid tenantId, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(SystemLogFilter filter, CancellationToken ct);
}
