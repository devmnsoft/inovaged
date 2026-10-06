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
            var nextDaily = DateTimeOffset.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var svc = scope.ServiceProvider.GetRequiredService<IRetentionRecalcService>();
                    var recovery = scope.ServiceProvider.GetRequiredService<AssistedRetentionRecovery>();
                    var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
                    var runDaily = DateTimeOffset.UtcNow >= nextDaily;
                    foreach (var tenantId in await catalog.GetActiveTenantIdsAsync(stoppingToken))
                    {
                        try
                        {
                            await recovery.RunBatchAsync(tenantId, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception)
                        {
                            _logger.LogError("Falha de temporalidade no tenant {TenantId}; pendências serão retomadas.", tenantId);
                        }
                        // An unavailable AI schema must not disable conventional retention.
                        if (runDaily)
                        {
                            try { await svc.RunAsync(tenantId, DueSoonDays, stoppingToken); }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                            catch (Exception) { _logger.LogError("Falha no recálculo diário. Tenant={TenantId}", tenantId); }
                        }
                    }
                    if (runDaily) nextDaily = DateTimeOffset.UtcNow.AddDays(1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha no RetentionDailyWorker.");
                }

                // Bounded durable recovery every 30 seconds; full recalculation remains daily.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("RetentionDailyWorker encerrado por solicitação de parada.");
        }
    }
}
