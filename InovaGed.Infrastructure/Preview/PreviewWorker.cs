using System.Text.Json;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Notifications;
using InovaGed.Application.Ged.Documents;
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
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private int _consecutiveCycleFailures;

    internal static TimeSpan CycleBackoff(int consecutiveFailures)
    {
        var steps = Math.Clamp(consecutiveFailures, 1, 6);
        return TimeSpan.FromSeconds(Math.Min(120, 5 * Math.Pow(2, steps - 1)));
    }

    public PreviewWorker(PreviewQueue queue, IServiceScopeFactory scopeFactory, ILogger<PreviewWorker> logger)
    {
        _queue = queue;
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
                var jobs = scope.ServiceProvider.GetRequiredService<IGedProcessingJobRepository>();
                var statusRepo = scope.ServiceProvider.GetRequiredService<IPreviewStatusRepository>();
                if (!await statusRepo.HasCanonicalStatusTypeAsync(stoppingToken))
                {
                    _consecutiveCycleFailures++;
                    var delay = CycleBackoff(_consecutiveCycleFailures);
                    _logger.LogError("Preview suspenso: ged.preview_status.status não é o enum ged.preview_processing_status com os rótulos PENDING, PROCESSING, READY, FAILED, CANCELED e ERROR. Aplique 2026_11_07_preview_status_enum_operator_contract. Nenhum documento foi alterado. DelaySeconds={DelaySeconds}", delay.TotalSeconds);
                    await Task.Delay(delay, stoppingToken);
                    continue;
                }

                var recovered = await jobs.RecoverAbandonedAsync("PREVIEW", PreviewQueue.AbandonedLease, stoppingToken);
                if (recovered > 0)
                    _logger.LogWarning("Previews em PROCESSING abandonado recuperados. Total={Total}", recovered);

                foreach (var expired in await jobs.ExpirePendingAsync("PREVIEW", PreviewQueue.PendingExpiration, stoppingToken))
                {
                    if (expired.DocumentVersionId is Guid versionId)
                    {
                        await statusRepo.UpsertAsync(new PreviewStatusUpsert
                        {
                            TenantId = expired.TenantId,
                            VersionId = versionId,
                            Status = PreviewProcessingStatus.Canceled,
                            ErrorMessage = "Solicitação de preview expirada.",
                            FinishedAt = DateTimeOffset.UtcNow
                        }, stoppingToken);
                    }
                }

                var claimed = await jobs.DequeueAsync(_workerId, 1, "PREVIEW", stoppingToken);
                if (claimed.Count == 0)
                {
                    if (_queue.TryTakeFallback(out var fallback))
                    {
                        await ProcessAsync(scope.ServiceProvider, fallback.TenantId, fallback.DocumentId, fallback.VersionId, fallback.StoragePath, fallback.FileName, fallback.RequestedAt, null, null, stoppingToken);
                        _consecutiveCycleFailures = 0;
                        continue;
                    }

                    await WaitForSignalAsync(stoppingToken);
                    _consecutiveCycleFailures = 0;
                    continue;
                }

                var job = claimed[0];
                using var heartbeat = new CancellationTokenSource();
                var heartbeatTask = TouchUntilStoppedAsync(jobs, job, heartbeat.Token);
                try
                {
                    await ProcessDurableJobAsync(scope.ServiceProvider, jobs, job, stoppingToken);
                }
                finally
                {
                    heartbeat.Cancel();
                    try { await heartbeatTask; } catch (OperationCanceledException) { }
                }

                _consecutiveCycleFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("PreviewWorker encerrado por solicitação de parada.");
            }
            catch (Exception ex)
            {
                _consecutiveCycleFailures++;
                var delay = CycleBackoff(_consecutiveCycleFailures);
                var supportCode = PreviewSupportCode.Create();
                _logger.LogError(ex, "Falha no ciclo do PreviewWorker. Os jobs seguintes continuam após o intervalo. SupportCode={SupportCode} Stage={Stage} Cause={Cause} ConsecutiveFailures={ConsecutiveFailures} DelaySeconds={DelaySeconds}", supportCode, "WORKER_CYCLE", ex.GetType().Name, _consecutiveCycleFailures, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task ProcessDurableJobAsync(IServiceProvider services, IGedProcessingJobRepository jobs, GedProcessingJobDto job, CancellationToken ct)
    {
        if (job.DocumentVersionId is not Guid versionId)
        {
            await jobs.FailAsync(job.TenantId, job.Id, "Job de preview sem versão documental.", TimeSpan.FromMinutes(10), CancellationToken.None);
            return;
        }

        var payload = ParsePayload(job.PayloadJson);
        var documentId = payload?.DocumentId is Guid parsed && parsed != Guid.Empty ? parsed : job.DocumentId ?? Guid.Empty;
        var storagePath = payload?.StoragePath;
        var fileName = payload?.FileName;
        if (string.IsNullOrWhiteSpace(storagePath) || string.IsNullOrWhiteSpace(fileName) || documentId == Guid.Empty)
        {
            var version = await LoadVersionAsync(services.GetRequiredService<IDbConnectionFactory>(), job.TenantId, versionId, ct);
            documentId = documentId == Guid.Empty ? version?.DocumentId ?? Guid.Empty : documentId;
            storagePath ??= version?.StoragePath;
            fileName ??= version?.FileName;
        }

        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(storagePath) || string.IsNullOrWhiteSpace(fileName))
        {
            await jobs.FailAsync(job.TenantId, job.Id, "Versão documental sem caminho de armazenamento para gerar preview.", TimeSpan.FromMinutes(10), CancellationToken.None);
            return;
        }

        DateTimeOffset? requestedAt = payload?.RequestedAt is DateTimeOffset requested && requested != default
            ? requested
            : job.CreatedAt != default ? job.CreatedAt : null;
        var requestedAtKind = payload?.RequestedAt is DateTimeOffset original && original != default
            ? null
            : job.CreatedAt != default ? "ORIGINAL" : null;

        try
        {
            var result = await ProcessAsync(services, job.TenantId, documentId, versionId, storagePath, fileName, requestedAt, requestedAtKind, job.Id, ct);
            switch (result.Outcome)
            {
                case "Ready":
                    await jobs.CompleteAsync(job.TenantId, job.Id, CancellationToken.None);
                    break;
                case "Canceled":
                    await jobs.CancelAsync(job.TenantId, job.Id, "Preview cancelado.", CancellationToken.None);
                    break;
                case "PersistencePending":
                    await jobs.FailAsync(job.TenantId, job.Id, "PDF gerado aguardando confirmação de persistência. A conversão não deve ser repetida.", result.StatusRecorded ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(60), CancellationToken.None);
                    break;
                case "FailureUnrecorded":
                    await jobs.FailAsync(job.TenantId, job.Id, PreviewSupportCode.JobMessage(result.SupportCode ?? PreviewSupportCode.Create()), TimeSpan.FromSeconds(60), CancellationToken.None);
                    break;
                default:
                    var conversionDelay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(1, job.AttemptCount)) * 15));
                    var conversionMessage = string.IsNullOrWhiteSpace(result.SupportCode)
                        ? "Falha de conversão do preview."
                        : PreviewSupportCode.JobMessage(result.SupportCode);
                    await jobs.FailAsync(job.TenantId, job.Id, conversionMessage, conversionDelay, CancellationToken.None);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await jobs.ReleaseAsync(job.TenantId, job.Id, _workerId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var supportCode = PreviewSupportCode.Create();
            _logger.LogError(ex, "Falha ao executar job de preview. SupportCode={SupportCode} Tenant={TenantId} Document={DocumentId} Version={VersionId} Job={JobId} Attempt={AttemptId} Stage={Stage} Cause={Cause}", supportCode, job.TenantId, documentId, versionId, job.Id, job.AttemptCount, "JOB_EXECUTION", ex.GetType().Name);
            try
            {
                await jobs.FailAsync(job.TenantId, job.Id, PreviewSupportCode.JobMessage(supportCode), TimeSpan.FromSeconds(60), CancellationToken.None);
            }
            catch (Exception recordEx)
            {
                _logger.LogError(recordEx, "Não foi possível registrar a falha do job. O lock permanece até a recuperação de PROCESSING abandonado. SupportCode={SupportCode} Tenant={TenantId} Document={DocumentId} Version={VersionId} Job={JobId} Stage={Stage} Cause={Cause}", supportCode, job.TenantId, documentId, versionId, job.Id, "JOB_FAILURE_PERSIST", recordEx.GetType().Name);
            }
        }
    }

    private async Task<PreviewExecutionResult> ProcessAsync(
        IServiceProvider services,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string storagePath,
        string fileName,
        DateTimeOffset? requestedAt,
        string? requestedAtKind,
        Guid? jobId,
        CancellationToken ct)
    {
        var preview = services.GetRequiredService<IPreviewGenerator>();
        var notifier = services.GetRequiredService<IPreviewNotificationService>();
        var statusRepo = services.GetRequiredService<IPreviewStatusRepository>();
        var attemptId = Guid.NewGuid();
        var processor = new PreviewJobProcessor(_logger);
        return await processor.ExecuteAsync(
            tenantId,
            documentId,
            versionId,
            jobId,
            requestedAt,
            requestedAtKind,
            attemptId,
            token => preview.GetOrCreatePreviewPdfAsync(tenantId, documentId, versionId, storagePath, fileName, token),
            async (write, token) =>
            {
                var delay = TimeSpan.FromMilliseconds(250);
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await statusRepo.UpsertAsync(write, token);
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (attempt < 3)
                    {
                        _logger.LogWarning(ex, "Falha temporária ao persistir status {Status} do preview. Tentativa de gravação {Attempt}/3. Tenant={TenantId} Version={VersionId} Job={JobId}", write.Status, attempt, tenantId, versionId, jobId);
                        await Task.Delay(delay, token);
                        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
                    }
                }
            },
            (status, url, message, token) => notifier.PublishAsync(tenantId, versionId, status, url, message, token),
            async token =>
            {
                var current = await statusRepo.GetAsync(tenantId, versionId, token);
                return current?.CancelRequested == true;
            },
            ct);
    }

    private async Task TouchUntilStoppedAsync(IGedProcessingJobRepository jobs, GedProcessingJobDto job, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(ct))
            await jobs.TouchLockAsync(job.TenantId, job.Id, _workerId, ct);
    }

    private async Task WaitForSignalAsync(CancellationToken stoppingToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        wait.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _queue.WaitForSignalAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static PreviewPayload? ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            return JsonSerializer.Deserialize<PreviewPayload>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<VersionFile?> LoadVersionAsync(IDbConnectionFactory db, Guid tenantId, Guid versionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<VersionFile>(new CommandDefinition("""
SELECT document_id AS "DocumentId", storage_path AS "StoragePath", file_name AS "FileName"
FROM ged.document_version
WHERE tenant_id = @tenantId AND id = @versionId
LIMIT 1;
""", new { tenantId, versionId }, cancellationToken: ct));
    }

    private sealed class PreviewPayload
    {
        public string? StoragePath { get; set; }
        public string? FileName { get; set; }
        public Guid DocumentId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
    }

    private sealed class VersionFile
    {
        public Guid DocumentId { get; set; }
        public string? StoragePath { get; set; }
        public string? FileName { get; set; }
    }
}
