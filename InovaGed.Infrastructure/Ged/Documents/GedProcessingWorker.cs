using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Search;
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
                await repo.RecoverAbandonedExceptAsync("PREVIEW", TimeSpan.FromMinutes(10), stoppingToken);
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
                var runtimeDependencyError = IsDiagnosticSourceFailure(ex);
                _logger.LogError(ex, runtimeDependencyError
                    ? "RuntimeDependencyError no GedProcessingWorker: System.Diagnostics.DiagnosticSource ausente; novo ciclo em 5 minutos."
                    : "Falha no loop do GedProcessingWorker; o worker continuará após backoff.");
                await Task.Delay(runtimeDependencyError ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(IServiceProvider services, IGedProcessingJobRepository repo, GedProcessingJobDto job, CancellationToken ct)
    {
        using var heartbeat = new CancellationTokenSource();
        var heartbeatTask = TouchUntilStoppedAsync(repo, job, heartbeat.Token);
        try
        {
            await ProcessClaimedJobAsync(services, repo, job, ct);
        }
        finally
        {
            heartbeat.Cancel();
            try { await heartbeatTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task TouchUntilStoppedAsync(IGedProcessingJobRepository repo, GedProcessingJobDto job, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await repo.TouchLockAsync(job.TenantId, job.Id, _workerId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao renovar o lease do job. JobId={JobId} Tenant={TenantId}", job.Id, job.TenantId);
            }
        }
    }

    private async Task ProcessClaimedJobAsync(IServiceProvider services, IGedProcessingJobRepository repo, GedProcessingJobDto job, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("GED processing job started JobId={JobId} Tenant={TenantId} Type={JobType} Version={VersionId}", job.Id, job.TenantId, job.JobType, job.DocumentVersionId);
            switch (job.JobType.ToUpperInvariant())
            {
                case "SECTOR_EXTRACT":
                    await services.GetRequiredService<InovaGed.Infrastructure.Sectors.SectorVersionProcessor>().ProcessAsync(job, ct);
                    // Processor commits suggestions and completion atomically with notifications.
                    return;
                case "OCR":
                    if (job.DocumentVersionId.HasValue)
                    {
                        var ocr = services.GetRequiredService<IOcrJobRepository>();
                        await ocr.EnqueueAsync(job.TenantId, job.DocumentVersionId.Value, Guid.Empty, false, ct);
                    }
                    break;
                case "PREVIEW":
                    _logger.LogWarning("Job PREVIEW devolvido ao PreviewWorker. JobId={JobId} Tenant={TenantId}", job.Id, job.TenantId);
                    await repo.ReleaseAsync(job.TenantId, job.Id, _workerId, ct);
                    return;
                case "SMART_INDEX":
                    if (job.DocumentId.HasValue)
                    {
                        var index = services.GetRequiredService<IGedSearchIndexService>();
                        await index.ReindexDocumentAsync(job.TenantId, job.DocumentId.Value, ct);
                    }
                    break;
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
            await repo.ReleaseAsync(job.TenantId, job.Id, _workerId, CancellationToken.None);
            if (job.JobType == "SECTOR_EXTRACT")
                await services.GetRequiredService<InovaGed.Infrastructure.Sectors.SectorVersionProcessor>().RefreshQueueStatusAsync(job, CancellationToken.None);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            await repo.ReleaseAsync(job.TenantId, job.Id, _workerId, CancellationToken.None);
        }
        catch (Exception ex) when (IsDiagnosticSourceFailure(ex))
        {
            _logger.LogCritical(ex, "RUNTIME_DEPENDENCY_ERROR Worker=GedProcessingWorker Dependency=System.Diagnostics.DiagnosticSource JobId={JobId}; o job não será marcado como falho e o worker tentará novamente após backoff.", job.Id);
            await repo.ReleaseAsync(job.TenantId, job.Id, _workerId, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
        catch (Exception ex)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(1, job.AttemptCount)) * 15));
            await repo.FailAsync(job.TenantId, job.Id, ex.Message, delay, CancellationToken.None);
            if (job.JobType == "SECTOR_EXTRACT")
                await services.GetRequiredService<InovaGed.Infrastructure.Sectors.SectorVersionProcessor>().RefreshQueueStatusAsync(job,CancellationToken.None);
            _logger.LogError(ex, "PROCESSING_JOB_FAILED JobId={JobId} Tenant={TenantId} Type={JobType} RetryDelaySeconds={RetryDelaySeconds}", job.Id, job.TenantId, job.JobType, delay.TotalSeconds);
        }
    }

    private static bool IsDiagnosticSourceFailure(Exception ex) =>
        ex.ToString().Contains("System.Diagnostics.DiagnosticSource", StringComparison.OrdinalIgnoreCase);
}
