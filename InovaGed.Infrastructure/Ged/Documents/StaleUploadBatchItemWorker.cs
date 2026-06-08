using InovaGed.Application.Ged.Documents;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents;

public sealed class StaleUploadBatchItemWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleUploadBatchItemWorker> _logger;
    private readonly ISchemaCompatibilityState _schemaState;

    public StaleUploadBatchItemWorker(IServiceScopeFactory scopeFactory, ILogger<StaleUploadBatchItemWorker> logger, ISchemaCompatibilityState schemaState)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _schemaState = schemaState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _schemaState.IsCompatibleAsync("UploadBatch", stoppingToken))
        {
            _logger.LogWarning("StaleUploadBatchItemWorker não iniciado: schema incompatível. Execute migrations.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IUploadBatchService>();
                var count = await service.MarkStaleReceivingItemsAsErrorAsync(TimeSpan.FromMinutes(10), stoppingToken);
                if (count > 0) _logger.LogWarning("Itens de upload RECEIVING antigos marcados como ERROR. Count={Count}", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na rotina MarkStaleReceivingItemsAsError.");
            }
        }
    }
}
