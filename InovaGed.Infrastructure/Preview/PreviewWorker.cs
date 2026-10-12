using InovaGed.Application;
using InovaGed.Application.Common.Notifications;
using InovaGed.Application.Preview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Preview;

public sealed class PreviewWorker : BackgroundService
{
    private readonly PreviewQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PreviewWorker> _logger;

    public PreviewWorker(PreviewQueue queue, IServiceScopeFactory scopeFactory, ILogger<PreviewWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                using var scope = _scopeFactory.CreateScope();
                var preview = scope.ServiceProvider.GetRequiredService<IPreviewGenerator>();
                var notifier = scope.ServiceProvider.GetRequiredService<IPreviewNotificationService>();
                var statusRepo = scope.ServiceProvider.GetRequiredService<IPreviewStatusRepository>();
                var requestedAt = job.RequestedAt == default ? DateTimeOffset.UtcNow : job.RequestedAt;
                try
                {
                    await PersistStatusWithRetryAsync(statusRepo, job, PreviewProcessingStatus.Processing, null, null, requestedAt, null, stoppingToken);
                    await PublishSafelyAsync(notifier, job, "PROCESSING", null, "Gerando preview", stoppingToken);
                    var path = await preview.GetOrCreatePreviewPdfAsync(job.TenantId, job.DocumentId, job.VersionId, job.StoragePath, job.FileName, stoppingToken);
                    await PersistStatusWithRetryAsync(statusRepo, job, PreviewProcessingStatus.Ready, path, null, requestedAt, DateTimeOffset.UtcNow, stoppingToken);
                    await PublishSafelyAsync(notifier, job, "READY", $"/Ged/PreviewVersion?versionId={job.VersionId}", null, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha preview async Version={VersionId}", job.VersionId);
                    var publicMessage = BuildPublicFailureMessage(ex);
                    try
                    {
                        await PersistStatusWithRetryAsync(statusRepo, job, PreviewProcessingStatus.Error, null, publicMessage, requestedAt, DateTimeOffset.UtcNow, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception statusEx)
                    {
                        _logger.LogError(statusEx, "Falha ao registrar status FAILED do preview. Tenant={TenantId} Version={VersionId}", job.TenantId, job.VersionId);
                    }

                    await PublishSafelyAsync(notifier, job, "ERROR", null, publicMessage, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("PreviewWorker encerrado por solicitação de parada.");
        }
    }

    private async Task PersistStatusWithRetryAsync(
        IPreviewStatusRepository statusRepo,
        PreviewQueue.PreviewJob job,
        PreviewProcessingStatus status,
        string? previewPath,
        string? errorMessage,
        DateTimeOffset requestedAt,
        DateTimeOffset? finishedAt,
        CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(250);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await statusRepo.UpsertAsync(job.TenantId, job.VersionId, status, previewPath, errorMessage, requestedAt, finishedAt, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning(ex, "Falha temporária ao persistir status {Status} do preview. Tentativa {Attempt}/3. Tenant={TenantId} Version={VersionId}", status, attempt, job.TenantId, job.VersionId);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
            }
        }
    }

    private async Task PublishSafelyAsync(IPreviewNotificationService notifier, PreviewQueue.PreviewJob job, string status, string? url, string? message, CancellationToken ct)
    {
        try
        {
            await notifier.PublishAsync(job.TenantId, job.VersionId, status, url, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao notificar status {Status} do preview. Tenant={TenantId} Version={VersionId}", status, job.TenantId, job.VersionId);
        }
    }

    private static string BuildPublicFailureMessage(Exception exception)
    {
        var supportId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"Não foi possível gerar o preview deste arquivo. O original continua disponível para download autorizado. Informe o suporte {supportId}.";
    }
}
