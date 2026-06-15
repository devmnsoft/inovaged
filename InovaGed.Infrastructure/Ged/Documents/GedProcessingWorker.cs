using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ocr;
using InovaGed.Application.Preview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class GedProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GedProcessingWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public GedProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<GedProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IGedProcessingJobRepository>();
                var jobs = await repo.DequeueAsync(_workerId, 5, stoppingToken);
                if (jobs.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                foreach (var job in jobs)
                {
                    await ProcessJobAsync(scope.ServiceProvider, repo, job, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("GedProcessingWorker encerrado por cancelamento normal.");
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("GedProcessingWorker encerrado por cancelamento normal.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no loop do GedProcessingWorker; o worker continuará após backoff.");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(IServiceProvider services, IGedProcessingJobRepository repo, GedProcessingJobDto job, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("GED processing job started JobId={JobId} Tenant={TenantId} Type={JobType} Version={VersionId}", job.Id, job.TenantId, job.JobType, job.DocumentVersionId);
            switch (job.JobType.ToUpperInvariant())
            {
                case "OCR":
                    if (job.DocumentVersionId.HasValue)
                    {
                        var ocr = services.GetRequiredService<IOcrJobRepository>();
                        await ocr.EnqueueAsync(job.TenantId, job.DocumentVersionId.Value, Guid.Empty, false, ct);
                    }
                    break;
                case "PREVIEW":
                    // PreviewWorker/PreviewQueue existente processa o cache quando a requisição/serviço especializado enfileira detalhes de storage.
                    break;
                case "SMART_INDEX":
                case "QUALITY":
                case "CLASSIFICATION":
                    // Mantém job rastreável; integrações específicas podem consumir estes tipos sem bloquear UploadBatch/File.
                    break;
                default:
                    _logger.LogWarning("Tipo de job GED desconhecido JobId={JobId} Type={JobType}", job.Id, job.JobType);
                    break;
            }

            await repo.CompleteAsync(job.TenantId, job.Id, ct);
            _logger.LogInformation("PROCESSING_JOB_COMPLETED JobId={JobId} Tenant={TenantId} Type={JobType}", job.Id, job.TenantId, job.JobType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await repo.CancelAsync(job.TenantId, job.Id, "Worker cancelado", CancellationToken.None);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            await repo.CancelAsync(job.TenantId, job.Id, "Worker cancelado", CancellationToken.None);
        }
        catch (Exception ex)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(1, job.AttemptCount)) * 15));
            await repo.FailAsync(job.TenantId, job.Id, ex.Message, delay, CancellationToken.None);
            _logger.LogError(ex, "PROCESSING_JOB_FAILED JobId={JobId} Tenant={TenantId} Type={JobType} RetryDelaySeconds={RetryDelaySeconds}", job.Id, job.TenantId, job.JobType, delay.TotalSeconds);
        }
    }
}
