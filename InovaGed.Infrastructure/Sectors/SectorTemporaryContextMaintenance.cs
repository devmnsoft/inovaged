using System.Diagnostics.Metrics;
using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Sectors;

public sealed class SectorTemporaryContextOptions
{
    public int TicketMinutes { get; set; } = 15;
    public int TicketsPerMinute { get; set; } = 30;
    public int MaxPendingTickets { get; set; } = 200;
    public int MaxRetainedTickets { get; set; } = 2000;
    public int RetentionHours { get; set; } = 24;
    public int BatchSize { get; set; } = 500;
    public int CleanupSeconds { get; set; } = 60;
}

public sealed class SectorTemporaryContextMaintenance(IDbConnectionFactory db, IOptions<SectorTemporaryContextOptions> options)
{
    private static readonly Meter Meter = new("InovaGed.Sectors.TemporaryContexts", "1.0");
    internal static readonly Counter<long> Created = Meter.CreateCounter<long>("sector.tickets.created");
    internal static readonly Counter<long> Rejected = Meter.CreateCounter<long>("sector.tickets.rejected");
    private static readonly Counter<long> Deleted = Meter.CreateCounter<long>("sector.contexts.expired_deleted");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("sector.contexts.cleanup_failures");
    private static readonly Histogram<long> Quantity = Meter.CreateHistogram<long>("sector.tickets.quantity");

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var policy = options.Value;
        await using var conn = await db.OpenAsync(ct);
        var total = 0;
        // Identifiers are fixed application constants. Each bounded transaction can be retried;
        // SKIP LOCKED lets other hosts and in-flight continuations proceed independently.
        foreach (var (table, timestamp) in new[]
        {
            ("sector_entry_ticket", "expires_at"), ("sector_open_code", "expires_at"),
            ("sector_open_session", "expires_at"), ("sector_open_request", "created_at")
        })
        {
            var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition($"""
with candidates as (
 select id from ged.{table}
 where {timestamp}<now()-make_interval(hours=>@hours)
 order by {timestamp},id limit @batch for update skip locked
), removed as (
 delete from ged.{table} t using candidates c where t.id=c.id returning t.id
)
select count(*)::int from removed
""", new { hours = policy.RetentionHours, batch = policy.BatchSize }, cancellationToken: ct));
            Deleted.Add(count, new KeyValuePair<string, object?>("kind", table));
            total += count;
        }
        var tickets = await conn.ExecuteScalarAsync<long>(new CommandDefinition("select count(*) from ged.sector_entry_ticket", cancellationToken: ct));
        Quantity.Record(tickets);
        return total;
    }
}

public sealed class SectorTemporaryContextWorker(IServiceScopeFactory scopes, IOptions<SectorTemporaryContextOptions> options,
    ILogger<SectorTemporaryContextWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay the first pass so startup and migrations do not compete with housekeeping.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.CleanupSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<SectorTemporaryContextMaintenance>().CleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    SectorTemporaryContextMaintenance.Failures.Add(1);
                    // Exception messages from a database must not expose input or clinical data.
                    logger.LogWarning("Limpeza de contextos temporários não concluída; nova tentativa no próximo ciclo.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
