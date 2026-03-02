using InovaGed.Application.Audit;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention
{
    public sealed class RetentionQueueJob : IRetentionQueueJob
    {
        private readonly IRetentionQueueRepository _repo;
        private readonly IAuditWriter _audit;
        private readonly ILogger<RetentionQueueJob> _logger;

        public RetentionQueueJob(IRetentionQueueRepository repo, IAuditWriter audit, ILogger<RetentionQueueJob> logger)
        {
            _repo = repo;
            _audit = audit;
            _logger = logger;
        }

        public async Task<int> RunAsync(Guid tenantId, Guid? userId, string? userName, CancellationToken ct)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var inserted = await _repo.GenerateQueueAsync(tenantId, now, ct);

                await _audit.WriteAsync(tenantId, userId, userName,
                    "Retention.Queue.Generate", "RetentionQueue", null, true,
                    new { inserted }, null, null, ct);

                return inserted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionQueueJob failed Tenant={Tenant}", tenantId);
                await _audit.WriteAsync(tenantId, userId, userName,
                    "Retention.Queue.Generate", "RetentionQueue", null, false,
                    new { error = ex.Message }, null, null, ct);
                throw;
            }
        }
    }
}