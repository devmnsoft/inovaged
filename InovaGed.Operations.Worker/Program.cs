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
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBackupOrchestrator>().ProcessDueJobsAsync(workerId, stoppingToken);
            var expired = await scope.ServiceProvider.GetRequiredService<IAiGovernanceStore>().ExpireReservationsAsync(stoppingToken);
            if (expired > 0) logger.LogInformation("Reservas de IA expiradas/reconciliadas: {Count}.", expired);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
