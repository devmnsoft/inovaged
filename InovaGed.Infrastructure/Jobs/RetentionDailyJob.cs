using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Jobs;

public sealed class RetentionDailyJob : BackgroundService
{
    private readonly IDbConnectionFactory _db;
    private readonly INotificationSender _notify;
    private readonly ILogger<RetentionDailyJob> _logger;

    public RetentionDailyJob(IDbConnectionFactory db, INotificationSender notify, ILogger<RetentionDailyJob> logger)
    {
        _db = db;
        _notify = notify;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // roda a cada 24h (primeira execução em 2 min)
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionDailyJob failed");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunOnce(CancellationToken ct)
    {
        _logger.LogInformation("RetentionDailyJob START");

        // ⚠️ Se você tem muitos tenants, você vai iterar tenants. Aqui está simples: todos docs de todos tenants.
        const string sqlUpdate = @"
update ged.document
set retention_status =
  case
    when retention_due_at is null then retention_status
    when retention_due_at < now() then 'OVERDUE'
    when retention_due_at < (now() + interval '30 days') then 'DUE_SOON'
    else 'OK'
  end
where true;
";

        const string sqlCounts = @"
select
  sum(case when retention_status='OVERDUE' then 1 else 0 end) as Overdue,
  sum(case when retention_status='DUE_SOON' then 1 else 0 end) as DueSoon
from ged.document;
";

        await using var conn = await _db.OpenAsync(ct);
        var updated = await conn.ExecuteAsync(sqlUpdate);
        var counts = await conn.QueryFirstAsync(sqlCounts);

        int overdue = (int)(counts.overdue ?? 0);
        int dueSoon = (int)(counts.duesoon ?? 0);

        await _notify.SendAsync("INOVAGED • Temporalidade (Job diário)",
            $"Atualizados={updated} • Vencidos={overdue} • A vencer (30d)={dueSoon}", ct);

        _logger.LogInformation("RetentionDailyJob END Updated={Updated} Overdue={Overdue} DueSoon={DueSoon}", updated, overdue, dueSoon);
    }
}