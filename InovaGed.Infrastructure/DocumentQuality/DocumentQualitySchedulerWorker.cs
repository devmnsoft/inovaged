using InovaGed.Application.DocumentQuality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.DocumentQuality;

public sealed class DocumentQualitySchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DocumentQualityOptions> _options;
    private readonly ILogger<DocumentQualitySchedulerWorker> _logger;

    public DocumentQualitySchedulerWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<DocumentQualityOptions> options, ILogger<DocumentQualitySchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _options.CurrentValue;
                if (!options.Enabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }

                var delay = CalculateDelay(options);
                _logger.LogInformation("DocumentQualitySchedulerWorker aguardando {Delay} para próxima execução às {RunAt} ({TimeZone}).", delay, options.RunAt, options.TimeZone);
                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IDocumentQualityAnalyzerService>();
                    await analyzer.AnalyzeAllAsync(options.TenantId, new DocumentQualityFilter
                    {
                        MaxDocuments = options.MaxDocumentsPerRun,
                        AnalyzeStorage = options.AnalyzeStorage,
                        AnalyzeLgpd = options.AnalyzeLgpd
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha na rotina diária de Qualidade Documental.");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("DocumentQualitySchedulerWorker encerrado por solicitação de parada.");
        }
    }

    private static TimeSpan CalculateDelay(DocumentQualityOptions options)
    {
        var timeZone = ResolveTimeZone(options.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        if (!TimeSpan.TryParse(options.RunAt, out var runAt)) runAt = new TimeSpan(19, 0, 0);
        var nextLocal = new DateTimeOffset(nowLocal.Date.Add(runAt), nowLocal.Offset);
        if (nextLocal <= nowLocal) nextLocal = nextLocal.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTime(nextLocal, TimeZoneInfo.Utc);
        return nextUtc - DateTimeOffset.UtcNow;
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        foreach (var candidate in id == "America/Belem" ? new[] { id, "E. South America Standard Time" } : new[] { id })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
