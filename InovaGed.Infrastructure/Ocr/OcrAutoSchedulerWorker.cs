using InovaGed.Application.Ocr;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrAutoSchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<OcrAutoScheduleOptions> _options;
    private readonly ILogger<OcrAutoSchedulerWorker> _logger;
    private readonly ISchemaCompatibilityState _schemaState;

    public OcrAutoSchedulerWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OcrAutoScheduleOptions> options,
        ILogger<OcrAutoSchedulerWorker> logger,
        ISchemaCompatibilityState schemaState)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _schemaState = schemaState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _schemaState.IsCompatibleAsync("OcrAutoSchedule", stoppingToken))
        {
            _logger.LogWarning("OCR Auto Scheduler não iniciado: schema incompatível do módulo de agendamento automático. Execute migrations.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = _options.CurrentValue;
                var nextRun = OcrAutoScheduleClock.CalculateNextRun(DateTimeOffset.UtcNow, options.RunAt, options.TimeZone);
                var delay = nextRun - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;

                _logger.LogInformation(
                    "OCR Auto Scheduler aguardando próxima execução. NextRun={NextRunUtc} NextRunLocal={NextRunLocal} Enabled={Enabled}",
                    nextRun,
                    OcrAutoScheduleClock.FormatLocal(nextRun, options.TimeZone),
                    options.Enabled);

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                options = _options.CurrentValue;
                if (!options.Enabled)
                {
                    _logger.LogInformation("OCR Auto Scheduler desabilitado por configuração. RunAt={RunAt} TimeZone={TimeZone}", options.RunAt, options.TimeZone);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IOcrAutoSchedulerService>();
                await service.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("OcrAutoSchedulerWorker encerrado por solicitação de parada.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado no OCR Auto Scheduler. O worker continuará no próximo ciclo.");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("OcrAutoSchedulerWorker encerrado por cancelamento.");
                    break;
                }
            }
        }
    }
}
