using Microsoft.Extensions.Logging;

namespace InovaGed.Application.Retention;

public sealed class RetentionRecalcService : IRetentionRecalcService
{
    private readonly IRetentionJobRepository _repo;
    private readonly ILogger<RetentionRecalcService> _logger;

    public RetentionRecalcService(IRetentionJobRepository repo, ILogger<RetentionRecalcService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<int> RunAsync(Guid tenantId, int dueSoonDays, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Retention recalculation START. Tenant={TenantId}", tenantId);
            var rows = await _repo.RecalculateAsync(tenantId, dueSoonDays, ct);
            _logger.LogInformation("Retention recalculation SUCCESS. Tenant={TenantId} Rows={Rows}", tenantId, rows);
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention recalculation ERROR. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task<int> RunOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Retention recalculation ONE START. Tenant={TenantId} Doc={DocId}", tenantId, documentId);
            var rows = await _repo.RecalculateOneAsync(tenantId, documentId, dueSoonDays, ct);
            _logger.LogInformation("Retention recalculation ONE SUCCESS. Tenant={TenantId} Doc={DocId} Rows={Rows}", tenantId, documentId, rows);
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention recalculation ONE ERROR. Tenant={TenantId} Doc={DocId}", tenantId, documentId);
            throw;
        }
    }
}