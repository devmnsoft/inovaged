using System;
using System.Threading;
using System.Threading.Tasks;
using InovaGed.Application.Retention;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Retention;

public sealed class RetentionDailyWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionDailyWorker> _logger;
    private readonly ISchemaCompatibilityState _schemaState;

    // ✅ operacional: tenant padrão
    private static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ✅ operacional: janela "vence em breve"
    private const int DueSoonDays = 30;

    public RetentionDailyWorker(IServiceScopeFactory scopeFactory, ILogger<RetentionDailyWorker> logger, ISchemaCompatibilityState schemaState)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _schemaState = schemaState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _schemaState.IsCompatibleAsync("Retention", stoppingToken))
        {
            _logger.LogWarning("RetentionDailyWorker não iniciado: schema incompatível. Execute migrations.");
            return;
        }

        _logger.LogInformation("RetentionDailyWorker iniciado.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    // ✅ use a interface (recomendado)
                    var svc = scope.ServiceProvider.GetRequiredService<IRetentionRecalcService>();

                    var rows = await svc.RunAsync(DefaultTenantId, DueSoonDays, stoppingToken);

                    _logger.LogInformation(
                        "RetentionDailyWorker execução OK. Tenant={TenantId} DueSoonDays={DueSoonDays} Rows={Rows}",
                        DefaultTenantId, DueSoonDays, rows);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha no RetentionDailyWorker.");
                }

                // ✅ roda 1x ao dia (pode trocar por cron depois)
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("RetentionDailyWorker encerrado por solicitação de parada.");
        }
    }
}
