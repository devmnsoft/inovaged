using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Identity;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanOverdueWorker : BackgroundService
{
    private readonly ILogger<LoanOverdueWorker> _logger;
    private readonly IServiceProvider _sp;
    private readonly ISchemaCompatibilityState _schemaState;
    private readonly LoanOverdueWorkerOptions _options;

    public LoanOverdueWorker(
        ILogger<LoanOverdueWorker> logger,
        IServiceProvider sp,
        ISchemaCompatibilityState schemaState,
        IOptions<LoanOverdueWorkerOptions> options)
    {
        _logger = logger;
        _sp = sp;
        _schemaState = schemaState;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _schemaState.IsCompatibleAsync("LoanOverdue", stoppingToken))
        {
            _logger.LogWarning("LoanOverdueWorker não iniciado: schema incompatível. Execute migrations.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();

                    var current = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
                    var commands = scope.ServiceProvider.GetRequiredService<ILoanCommands>();

                    var tenantId = _options.TenantId != Guid.Empty ? _options.TenantId : current.TenantId;
                    if (tenantId == Guid.Empty)
                    {
                        _logger.LogWarning(
                            "LoanOverdueWorker ignorado: TenantId não configurado. Configure Workers:LoanOverdue:TenantId ou desative com Workers:LoanOverdue:Enabled=false.");
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }

                    var res = await commands.RegisterOverdueEventsAsync(tenantId, current.UserId, stoppingToken);
                    if (res.IsSuccess)
                        _logger.LogInformation("Overdue registrados. Tenant={Tenant} Count={Count}", tenantId, res.Value);
                    else
                        _logger.LogWarning("Overdue falhou. Tenant={Tenant} Err={Err}", tenantId, res.ErrorMessage);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LoanOverdueWorker falhou durante a execução; nova tentativa será feita no próximo ciclo.");
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("LoanOverdueWorker finalizado por cancelamento da aplicação.");
        }
    }
}
