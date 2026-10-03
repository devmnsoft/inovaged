using InovaGed.Application;
using InovaGed.Application.Continuity;
using InovaGed.Infrastructure;
using InovaGed.Application.ArtificialIntelligence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService();
builder.Services.AddSystemd();
builder.Services.AddInovaGedApplication(builder.Configuration).AddInovaGedInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OperationsWorker>();
await builder.Build().RunAsync();

public sealed class OperationsWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Operations:WorkerEnabled", false)) { logger.LogInformation("Operations worker desabilitado por configuração."); return; }
        var workerId = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");
        var backupFailures = 0; var aiFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IBackupOrchestrator>().ProcessDueJobsAsync(workerId, stoppingToken);
                if (backupFailures > 0) logger.LogInformation("Ciclo de backup recuperou após {Count} falha(s) consecutiva(s).", backupFailures);
                backupFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                backupFailures++;
                logger.LogError(ex, "Falha no ciclo de backup ({Count} consecutiva(s)); a expiração de IA segue em ciclo independente.", backupFailures);
            }
            try
            {
                var ai = scope.ServiceProvider.GetRequiredService<IAiGovernanceStore>();
                var expired = await ai.ExpireReservationsAsync(stoppingToken);
                if (expired > 0) logger.LogInformation("Reservas de IA expiradas/reconciliadas: {Count}.", expired);
                var health = await ai.GetRecoveryHealthAsync(stoppingToken);
                if (health.RemoteOutcomeUnknown > 0 || health.Expired > 0 || health.PendingExpired > 0)
                    logger.LogWarning("Saúde da recuperação de IA: RemoteOutcomeUnknown={RemoteOutcomeUnknown}, Expiradas={Expired}, PendentesExpirados={PendingExpired}.", health.RemoteOutcomeUnknown, health.Expired, health.PendingExpired);
                if (aiFailures > 0) logger.LogInformation("Expiração de IA recuperou após {Count} falha(s) consecutiva(s).", aiFailures);
                aiFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                aiFailures++;
                logger.LogError(ex, "Falha na expiração/recuperação de IA ({Count} consecutiva(s)); o ciclo de backup segue independente.", aiFailures);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
