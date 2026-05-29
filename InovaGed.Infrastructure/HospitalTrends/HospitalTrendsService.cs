using System.Security.Cryptography;
using System.Text;
using InovaGed.Application.HospitalAnalytics;
using InovaGed.Application.HospitalTrends;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;
using AppDocumentSnippetDto = InovaGed.Application.HospitalTrends.DocumentSnippetDto;
using AnalyticsDocumentSnippetDto = InovaGed.Application.HospitalAnalytics.DocumentSnippetDto;

namespace InovaGed.Infrastructure.HospitalTrends;

public sealed class HospitalTrendsService : IHospitalTrendsService
{
    private const int DefaultTopDocuments = 1000;
    private const int MaxTopDocuments = 5000;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly string[] CriticalTerms = ["óbito", "sepse", "UTI"];

    private readonly IHospitalOcrAnalyticsService _analytics;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HospitalTrendsService> _logger;

    public HospitalTrendsService(IHospitalOcrAnalyticsService analytics, IMemoryCache cache, ILogger<HospitalTrendsService> logger)
    {
        _analytics = analytics;
        _cache = cache;
        _logger = logger;
    }

    public async Task<HospitalTrendsDashboardDto> GetDashboardAsync(HospitalTrendsFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        var cacheKey = BuildCacheKey(f, "dashboard");
        if (!f.RefreshCache && _cache.TryGetValue(cacheKey, out HospitalTrendsDashboardDto? cached) && cached is not null)
            return cached;

        try
        {
            var current = await _analytics.BuildSnapshotAsync(ToAnalyticsFilter(f, f.From!.Value, f.To!.Value), ct);
            var previous = await _analytics.BuildSnapshotAsync(ToAnalyticsFilter(f, f.CompareFrom!.Value, f.CompareTo!.Value), ct);
            var dictionary = FilterDictionary(f.Category);
            var currentTerms = await _analytics.AnalyzeTermsAsync(current, dictionary, ct);
            var previousTerms = await _analytics.AnalyzeTermsAsync(previous, dictionary, ct);

            var termTrends = BuildTermTrends(currentTerms, previousTerms).ToList();
            var sectorTrends = BuildSectorTrends(current, previous, f.Top).ToList();
            var operational = BuildOperationalTrends(current, previous).ToList();
            var alerts = BuildAlerts(termTrends, sectorTrends, operational, current).ToList();
            var warnings = current.Warnings.Concat(previous.Warnings)
                .Append("Indicadores baseados em OCR documental. Utilizar para apoio gerencial e estatístico, não como diagnóstico clínico.")
                .Distinct()
                .ToList();

            var dashboard = new HospitalTrendsDashboardDto
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                PeriodLabel = BuildPeriodLabel(f.From.Value, f.To.Value),
                ComparePeriodLabel = BuildPeriodLabel(f.CompareFrom.Value, f.CompareTo.Value),
                TotalDocumentsCurrent = current.TotalDocuments,
                TotalDocumentsPrevious = previous.TotalDocuments,
                VariationPercent = Variation(current.TotalDocuments, previous.TotalDocuments),
                TermTrends = termTrends,
                Alerts = alerts,
                SectorTrends = sectorTrends,
                OperationalTrends = operational,
                TopFolders = BuildRanking(current.Rows.Select(x => x.Folder), current.Rows.Count),
                TopDocumentTypes = BuildRanking(current.Rows.Select(x => x.DocumentType), current.Rows.Count),
                TotalAlerts = alerts.Count,
                CriticalAlerts = alerts.Count(a => a.Severity is "Alto" or "Crítico"),
                WarningAlerts = alerts.Count(a => a.Severity is "Médio" or "Médio/Alto"),
                Warnings = warnings
            };

            _cache.Set(cacheKey, dashboard, CacheTtl);
            return dashboard;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P08")
        {
            _logger.LogError(ex, "Erro de tipagem de parâmetro SQL em HospitalTrends via HospitalOcrAnalytics. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis por erro de consulta. Verifique os logs técnicos.");
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao gerar Central de Alertas e Tendências Hospitalares via HospitalOcrAnalytics. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Não foi possível consultar a base documental real no momento.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao gerar Central de Alertas e Tendências Hospitalares via HospitalOcrAnalytics. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis no momento. Informe o código de rastreio ao suporte.");
        }
    }

    public async Task<IReadOnlyList<TrendKpiDto>> GetTermTrendsAsync(HospitalTrendsFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).TermTrends;

    public async Task<IReadOnlyList<HospitalAlertDto>> GetAlertsAsync(HospitalTrendsFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).Alerts;

    public async Task<IReadOnlyList<SectorTrendDto>> GetSectorTrendsAsync(HospitalTrendsFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).SectorTrends;

    public async Task<IReadOnlyList<OperationalTrendDto>> GetOperationalTrendsAsync(HospitalTrendsFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).OperationalTrends;

    private static IEnumerable<TrendKpiDto> BuildTermTrends(IReadOnlyList<TermMatchDto> currentTerms, IReadOnlyList<TermMatchDto> previousTerms)
    {
        var previousByTerm = previousTerms.ToDictionary(x => Key(x.Term, x.Category), StringComparer.OrdinalIgnoreCase);
        foreach (var current in currentTerms)
        {
            previousByTerm.TryGetValue(Key(current.Term, current.Category), out var previous);
            var previousCount = previous?.DocumentCount ?? 0;
            var variation = Variation(current.DocumentCount, previousCount);
            var direction = Direction(current.DocumentCount, previousCount, variation);
            yield return new TrendKpiDto
            {
                Term = current.Term,
                Category = current.Category,
                CurrentCount = current.DocumentCount,
                PreviousCount = previousCount,
                VariationPercent = variation,
                TrendDirection = direction,
                RiskLevel = RiskLevel(current.Term, current.Category, current.DocumentCount, variation, direction),
                Interpretation = BuildTermInterpretation(current.Term, current.DocumentCount, previousCount, variation, direction),
                Examples = current.Examples.Select(MapSnippet).ToList()
            };
        }
    }

    private static IEnumerable<SectorTrendDto> BuildSectorTrends(HospitalOcrAnalyticsSnapshotDto current, HospitalOcrAnalyticsSnapshotDto previous, int top)
    {
        var currentGroups = current.Rows.GroupBy(r => SafeLabel(string.IsNullOrWhiteSpace(r.Sector) ? r.Folder : r.Sector, "Não informado")).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var previousGroups = previous.Rows.GroupBy(r => SafeLabel(string.IsNullOrWhiteSpace(r.Sector) ? r.Folder : r.Sector, "Não informado")).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var (sector, rows) in currentGroups.OrderByDescending(x => x.Value.Count).Take(top <= 0 ? 20 : Math.Min(top, 100)))
        {
            previousGroups.TryGetValue(sector, out var previousCount);
            var variation = Variation(rows.Count, previousCount);
            var pendingOcr = rows.Count(r => !string.Equals(r.OcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) && !string.Equals(r.OcrStatus, "DONE", StringComparison.OrdinalIgnoreCase) && !string.Equals(r.OcrStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase));
            var unclassified = rows.Count(r => !r.IsClassified);
            yield return new SectorTrendDto
            {
                Sector = sector,
                CurrentDocuments = rows.Count,
                PreviousDocuments = previousCount,
                VariationPercent = variation,
                PendingOcr = pendingOcr,
                Unclassified = unclassified,
                Interpretation = BuildSectorInterpretation(sector, rows.Count, previousCount, variation, pendingOcr, unclassified)
            };
        }
    }

    private static IEnumerable<OperationalTrendDto> BuildOperationalTrends(HospitalOcrAnalyticsSnapshotDto current, HospitalOcrAnalyticsSnapshotDto previous)
    {
        yield return BuildOperational("Volume documental", current.TotalDocuments, previous.TotalDocuments, "Avaliar capacidade operacional conforme variação de volume documental.");
        yield return BuildOperational("Documentos com OCR", current.DocumentsWithOcr, previous.DocumentsWithOcr, "Manter monitoramento de cobertura OCR e priorizar filas pendentes.");
        yield return BuildOperational("Pendências de OCR", current.DocumentsWithoutOcr, previous.DocumentsWithoutOcr, "Reprocessar pendências e acompanhar erros recorrentes.");
        yield return BuildOperational("Erros de OCR", current.OcrErrors, previous.OcrErrors, "Auditar documentos com falha OCR e qualidade de imagem.");
        yield return BuildOperational("Documentos sem classificação", current.UnclassifiedDocuments, previous.UnclassifiedDocuments, "Acionar responsáveis pela classificação arquivística e metadados obrigatórios.");
    }

    private static IEnumerable<HospitalAlertDto> BuildAlerts(IReadOnlyList<TrendKpiDto> terms, IReadOnlyList<SectorTrendDto> sectors, IReadOnlyList<OperationalTrendDto> operational, HospitalOcrAnalyticsSnapshotDto current)
    {
        foreach (var term in terms.Where(t => t.RiskLevel is "Alto" or "Médio/Alto").Take(10))
        {
            yield return Alert($"TERM:{term.Category}:{term.Term}", $"Tendência relevante: {term.Term}", term.Category, term.RiskLevel, term.Interpretation, "Validar amostra documental e avaliar plano de ação com a área responsável.", term.CurrentCount, term.Examples);
        }

        if (current.OcrErrors > 0)
            yield return Alert("OCR:ERRORS", "Erros de OCR no período", "OPERACIONAL", "Alto", $"Há {current.OcrErrors:N0} documentos com erro de OCR no período atual.", "Reprocessar erros e avaliar qualidade dos arquivos de origem.", current.OcrErrors, []);

        if (current.TotalDocuments > 0 && Percent(current.UnclassifiedDocuments, current.TotalDocuments) > 30)
            yield return Alert("OP:UNCLASSIFIED", "Classificação arquivística atrasada", "OPERACIONAL", "Médio/Alto", $"{Percent(current.UnclassifiedDocuments, current.TotalDocuments):N1}% dos documentos do período estão sem classificação.", "Priorizar classificação para reduzir risco de retenção, busca e governança documental.", current.UnclassifiedDocuments, []);

        var totalPending = sectors.Sum(s => s.PendingOcr + s.Unclassified);
        foreach (var sector in sectors.Where(s => totalPending > 0 && (s.PendingOcr + s.Unclassified) * 100m / totalPending > 40).Take(3))
            yield return Alert($"SECTOR:{sector.Sector}", $"Pendências concentradas em {sector.Sector}", "SETOR", "Médio", $"O setor/pasta concentra {((sector.PendingOcr + sector.Unclassified) * 100m / totalPending):N1}% das pendências analisadas.", "Revisar fila, classificação e rotina de saneamento documental do setor/pasta.", sector.PendingOcr + sector.Unclassified, []);

        foreach (var op in operational.Where(o => o.Status == "Atenção").Take(3))
            yield return Alert($"OP:{op.Indicator}", op.Indicator, "OPERACIONAL", "Médio", $"Indicador em atenção: {op.CurrentValue:N0} no período atual ({op.VariationPercent:N1}%).", op.Recommendation, op.CurrentValue, []);
    }

    private static HospitalAlertDto Alert(string key, string title, string category, string severity, string description, string recommendation, int count, List<AppDocumentSnippetDto> examples) => new()
    {
        Id = StableGuid(key),
        Title = title,
        Category = category,
        Severity = severity,
        Description = description,
        Recommendation = recommendation,
        RelatedDocumentCount = count,
        Examples = examples
    };

    private static OperationalTrendDto BuildOperational(string indicator, int current, int previous, string recommendation)
    {
        var variation = Variation(current, previous);
        return new OperationalTrendDto
        {
            Indicator = indicator,
            CurrentValue = current,
            PreviousValue = previous,
            VariationPercent = variation,
            Recommendation = recommendation,
            Status = Math.Abs(variation) > 30 && current > previous ? "Atenção" : "Informativo"
        };
    }

    private static IReadOnlyList<TermDictionaryItemDto> FilterDictionary(string? category)
        => string.IsNullOrWhiteSpace(category)
            ? HospitalTermDictionary.All
            : HospitalTermDictionary.All.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    private static HospitalOcrAnalyticsFilter ToAnalyticsFilter(HospitalTrendsFilter f, DateTime from, DateTime to) => new()
    {
        TenantId = f.TenantId,
        From = from.Date,
        To = to.Date.AddDays(1),
        FolderId = f.FolderId,
        Sector = f.Sector,
        DocumentType = f.DocumentType,
        Top = f.Top,
        RefreshCache = f.RefreshCache
    };

    private static HospitalTrendsFilter NormalizeFilter(HospitalTrendsFilter filter)
    {
        var today = DateTime.UtcNow.Date;
        var from = filter.From?.Date ?? today.AddDays(-29);
        var to = filter.To?.Date ?? today;
        if (from > to) (from, to) = (to, from);
        var length = (to - from).Days + 1;
        var compareTo = filter.CompareTo?.Date ?? from.AddDays(-1);
        var compareFrom = filter.CompareFrom?.Date ?? compareTo.AddDays(-(length - 1));
        if (compareFrom > compareTo) (compareFrom, compareTo) = (compareTo, compareFrom);
        return new HospitalTrendsFilter { TenantId = filter.TenantId, From = from, To = to, CompareFrom = compareFrom, CompareTo = compareTo, FolderId = filter.FolderId, Sector = NullIfWhiteSpace(filter.Sector), DocumentType = NullIfWhiteSpace(filter.DocumentType), Category = NullIfWhiteSpace(filter.Category), Top = NormalizeTop(filter.Top), RefreshCache = filter.RefreshCache };
    }

    private static AppDocumentSnippetDto MapSnippet(AnalyticsDocumentSnippetDto x) => new() { DocumentId = x.DocumentId, VersionId = x.VersionId, Title = x.Title, Folder = x.Folder, Snippet = x.Snippet, CreatedAt = x.CreatedAt };
    private static string BuildCacheKey(HospitalTrendsFilter f, string part) => $"HospitalTrends:{part}:{f.TenantId}:{f.From:yyyyMMdd}:{f.To:yyyyMMdd}:{f.CompareFrom:yyyyMMdd}:{f.CompareTo:yyyyMMdd}:{f.FolderId}:{f.Sector}:{f.DocumentType}:{f.Category}:{f.Top}";
    private static string BuildPeriodLabel(DateTime from, DateTime to) => $"{from:dd/MM/yyyy} a {to:dd/MM/yyyy}";
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int NormalizeTop(int top) => top <= 0 ? DefaultTopDocuments : Math.Min(top, MaxTopDocuments);
    private static decimal Percent(int part, int total) => total <= 0 ? 0 : Math.Round(part * 100m / total, 2);
    private static decimal Variation(int current, int previous) => previous == 0 ? (current > 0 ? 100 : 0) : Math.Round((current - previous) * 100m / previous, 2);
    private static string Direction(int current, int previous, decimal variation) => previous == 0 && current > 0 ? "Novo" : variation > 5 ? "Crescimento" : variation < -5 ? "Queda" : "Estável";
    private static string Key(string term, string category) => $"{category}:{term}";
    private static string SafeLabel(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string TrendVerb(string direction) => direction switch { "Crescimento" => "cresceram", "Queda" => "caíram", _ => "permaneceram estáveis em" };
    private static string BuildTermInterpretation(string term, int current, int previous, decimal variation, string direction) => direction == "Novo" ? $"Menções a '{term}' surgiram no período atual em {current:N0} documentos analisados." : $"Menções a '{term}' {TrendVerb(direction)} {Math.Abs(variation):N1}% nos documentos analisados em relação ao período anterior ({current:N0} contra {previous:N0}).";
    private static string BuildSectorInterpretation(string sector, int current, int previous, decimal variation, int pendingOcr, int unclassified) => $"O setor/pasta {sector} registrou {current:N0} documentos OCR no período ({TrendVerb(Direction(current, previous, variation))} {Math.Abs(variation):N1}%), com {pendingOcr:N0} pendências OCR e {unclassified:N0} sem classificação.";
    private static List<RankingKpiDto> BuildRanking(IEnumerable<string?> values, int total) => values.Select(v => SafeLabel(v, "Não informado")).GroupBy(v => v).OrderByDescending(g => g.Count()).Take(10).Select(g => new RankingKpiDto { Label = g.Key, Value = g.Count(), Percentage = Percent(g.Count(), total) }).ToList();
    private static string RiskLevel(string term, string category, int current, decimal variation, string direction)
    {
        if (CriticalTerms.Any(x => x.Equals(term, StringComparison.OrdinalIgnoreCase)) && variation > 30) return "Alto";
        if ((term.Equals("glosa", StringComparison.OrdinalIgnoreCase) || term.Equals("faturamento", StringComparison.OrdinalIgnoreCase)) && variation > 0) return "Alto";
        if (current >= 5 && variation > 50) return "Alto";
        if (direction == "Novo" && current >= 5) return "Médio/Alto";
        if (category.Contains("JURÍDICO", StringComparison.OrdinalIgnoreCase) && current >= 3) return "Médio";
        return current >= 3 ? "Médio" : "Baixo";
    }
    private static Guid StableGuid(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    private static HospitalTrendsDashboardDto EmptyDashboard(string warning) => new() { GeneratedAt = DateTimeOffset.UtcNow, Warnings = [warning] };
}
