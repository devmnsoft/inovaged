using InovaGed.Application.Ged.Loans;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanOverdueWorker : BackgroundService
{
    private readonly ILogger<LoanOverdueWorker> _logger;
    private readonly IServiceProvider _sp;
    private readonly ISchemaCompatibilityState _schemaState;
    private readonly LoanOverdueWorkerOptions _options;
    private bool _loanHistoryWarningLogged;
    private bool _runtimeSchemaWarningLogged;

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
                    if (_runtimeSchemaWarningLogged)
                    {
                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }

                    using var scope = _sp.CreateScope();

                    var overdue = scope.ServiceProvider.GetRequiredService<ILoanOverdueService>();
                    var db = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
                    var tenants = new List<Guid>();
                    if (_options.TenantId != Guid.Empty) tenants.Add(_options.TenantId);
                    else
                    {
                        await using var conn = await db.OpenAsync(stoppingToken);
                        if (await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select to_regclass('ged.tenant')::text", cancellationToken: stoppingToken)) is not null)
                            tenants.AddRange(await conn.QueryAsync<Guid>(new CommandDefinition("select id from ged.tenant where coalesce(reg_status,'A')='A'", cancellationToken: stoppingToken)));
                    }
                    if (tenants.Count == 0) { _logger.LogWarning("LoanOverdueWorker sem tenants ativos configurados."); await Task.Delay(interval, stoppingToken); continue; }

                    if (!await _schemaState.IsCompatibleAsync("LoansHistory", stoppingToken))
                    {
                        if (!_loanHistoryWarningLogged)
                        {
                            _logger.LogWarning("LoanOverdueWorker não executado: histórico de Loans não configurado.");
                            _loanHistoryWarningLogged = true;
                        }

                        await Task.Delay(interval, stoppingToken);
                        continue;
                    }

                    var started = DateTimeOffset.UtcNow; var processed = 0; var failures = 0;
                    foreach (var tenantId in tenants)
                    {
                        try { processed += await overdue.RunAsync(tenantId, null, stoppingToken); }
                        catch (Exception ex) { failures++; _logger.LogError(ex, "Falha segura na rotina de vencidos do tenant."); }
                    }
                    _logger.LogInformation("LoanOverdueWorker concluído. Started={Started} Finished={Finished} Tenants={Tenants} Processed={Processed} Collected={Collected} Failures={Failures}", started, DateTimeOffset.UtcNow, tenants.Count, processed, 0, failures);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedFunction)
                {
                    if (!_runtimeSchemaWarningLogged)
                    {
                        _logger.LogWarning("LoanOverdueWorker não executado: schema de empréstimos incompleto ({SqlState}, {DatabaseMessage}). Execute database/apply_all_required_migrations.sql. Novos ciclos serão ignorados até reiniciar a aplicação após a migração.", ex.SqlState, ex.MessageText);
                        _runtimeSchemaWarningLogged = true;
                    }
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
