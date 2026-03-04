using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanOverdueWorker : BackgroundService
{
    private readonly ILogger<LoanOverdueWorker> _logger;
    private readonly IServiceProvider _sp;

    public LoanOverdueWorker(ILogger<LoanOverdueWorker> logger, IServiceProvider sp)
    {
        _logger = logger;
        _sp = sp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();

                var current = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
                var commands = scope.ServiceProvider.GetRequiredService<ILoanCommands>();

                var res = await commands.RegisterOverdueEventsAsync(current.TenantId, current.UserId, stoppingToken);
                if (res.IsSuccess)
                    _logger.LogInformation("Overdue registrados. Tenant={Tenant} Count={Count}", current.TenantId, res.Value);
                else
                    _logger.LogWarning("Overdue falhou. Tenant={Tenant} Err={Err}", current.TenantId, res.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LoanOverdueWorker crashed");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}