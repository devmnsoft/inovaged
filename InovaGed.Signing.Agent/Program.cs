using System.Net;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<LoopbackSigningAgent>();
await builder.Build().RunAsync();

internal sealed class LoopbackSigningAgent(ILogger<LoopbackSigningAgent> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("InovaGED Signing Agent scaffold inicializado somente em loopback. Nenhuma chave privada, PIN ou documento integral deve ser transmitido ao servidor.");
        _ = IPAddress.Loopback;
        return Task.CompletedTask;
    }
}
