using InovaGed.Application;
using InovaGed.Application.Common.Notifications;
using InovaGed.Application.Preview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace InovaGed.Infrastructure.Preview;
public sealed class PreviewWorker : BackgroundService
{
    private readonly PreviewQueue _queue; private readonly IServiceScopeFactory _scopeFactory; private readonly ILogger<PreviewWorker> _logger;
    public PreviewWorker(PreviewQueue queue, IServiceScopeFactory scopeFactory, ILogger<PreviewWorker> logger){_queue=queue;_scopeFactory=scopeFactory;_logger=logger;}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var preview = scope.ServiceProvider.GetRequiredService<IPreviewGenerator>();
            var notifier = scope.ServiceProvider.GetRequiredService<IPreviewNotificationService>();
            var statusRepo = scope.ServiceProvider.GetRequiredService<IPreviewStatusRepository>();
            try
            {
                await statusRepo.UpsertAsync(job.TenantId, job.VersionId, PreviewProcessingStatus.Processing, null, null, null, null, stoppingToken);
                await notifier.PublishAsync(job.TenantId, job.VersionId, "PROCESSING", null, "Gerando preview", stoppingToken);
                var path = await preview.GetOrCreatePreviewPdfAsync(job.TenantId, job.DocumentId, job.VersionId, job.StoragePath, job.FileName, stoppingToken);
                await statusRepo.UpsertAsync(job.TenantId, job.VersionId, PreviewProcessingStatus.Ready, path, null, null, DateTimeOffset.UtcNow, stoppingToken);
                await notifier.PublishAsync(job.TenantId, job.VersionId, "READY", $"/storage/{path}", null, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha preview async Version={VersionId}", job.VersionId);
                await statusRepo.UpsertAsync(job.TenantId, job.VersionId, PreviewProcessingStatus.Error, null, ex.Message, null, DateTimeOffset.UtcNow, stoppingToken);
                await notifier.PublishAsync(job.TenantId, job.VersionId, "ERROR", null, ex.Message, stoppingToken);
            }
        }
    }
}
