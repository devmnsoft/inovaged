using InovaGed.Application.HospitalAnalytics;
using InovaGed.Application.HospitalIntelligence;
using Microsoft.Extensions.Logging;
using Npgsql;
using AppDocumentSnippetDto = InovaGed.Application.HospitalIntelligence.DocumentSnippetDto;
using AnalyticsDocumentSnippetDto = InovaGed.Application.HospitalAnalytics.DocumentSnippetDto;

namespace InovaGed.Infrastructure.HospitalIntelligence;

public sealed class HospitalIntelligenceService : IHospitalIntelligenceService
{
    private const int DefaultTopDocuments = 1000;
    private const int MaxTopDocuments = 5000;

    private readonly IHospitalOcrAnalyticsService _analytics;
    private readonly ILogger<HospitalIntelligenceService> _logger;

    public HospitalIntelligenceService(IHospitalOcrAnalyticsService analytics, ILogger<HospitalIntelligenceService> logger)
    {
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<HospitalIntelligenceDashboardDto> GetDashboardAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        try
        {
            var snapshot = await _analytics.BuildSnapshotAsync(ToAnalyticsFilter(f), ct);
            var terms = await _analytics.AnalyzeTermsAsync(snapshot, HospitalTermDictionary.All, ct);
            var money = await _analytics.AnalyzeMoneySignalsAsync(snapshot, ct);
            return BuildDashboard(snapshot, terms, money, f.Top);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P08")
        {
            _logger.LogError(ex, "Erro de tipagem de parâmetro SQL em HospitalIntelligence via HospitalOcrAnalytics. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis por erro de consulta. Verifique os logs técnicos.");
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao gerar Inteligência Hospitalar via HospitalOcrAnalytics. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Não foi possível carregar os indicadores devido a indisponibilidade temporária da base de dados.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao gerar Inteligência Hospitalar via HospitalOcrAnalytics. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis no momento. Informe o código de rastreio ao suporte.");
        }
    }

    public async Task<IReadOnlyList<OcrKpiDto>> GetOcrKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).OcrKpis;

    public async Task<IReadOnlyList<ClinicalTermKpiDto>> GetClinicalTermKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).ClinicalTerms;

    public async Task<IReadOnlyList<FinancialDocumentKpiDto>> GetFinancialKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).FinancialKpis;

    public async Task<IReadOnlyList<OperationalKpiDto>> GetOperationalKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).OperationalKpis;

    public async Task<IReadOnlyList<RiskAlertKpiDto>> GetRiskAlertsAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => (await GetDashboardAsync(filter, ct)).Alerts;

    private static HospitalIntelligenceDashboardDto BuildDashboard(HospitalOcrAnalyticsSnapshotDto snapshot, IReadOnlyList<TermMatchDto> terms, IReadOnlyList<MoneySignalDto> money, int top)
    {
        var clinical = BuildClinicalTerms(terms, snapshot.DocumentsWithOcr).ToList();
        var financial = BuildFinancialKpis(terms, money).ToList();
        var operational = BuildOperationalKpis(snapshot).ToList();
        var alerts = BuildRiskAlerts(snapshot, clinical, financial).ToList();
        var dto = new HospitalIntelligenceDashboardDto
        {
            GeneratedAt = snapshot.GeneratedAt,
            TotalDocuments = snapshot.TotalDocuments,
            DocumentsWithOcr = snapshot.DocumentsWithOcr,
            DocumentsWithoutOcr = snapshot.DocumentsWithoutOcr,
            OcrPending = snapshot.OcrPending > 0 ? snapshot.OcrPending : snapshot.DocumentsWithoutOcr,
            OcrProcessing = snapshot.OcrProcessing,
            OcrCompleted = snapshot.OcrCompleted,
            OcrErrors = snapshot.OcrErrors,
            OcrCancelled = snapshot.OcrCancelled,
            UnclassifiedDocuments = snapshot.UnclassifiedDocuments,
            ClassifiedDocuments = snapshot.ClassifiedDocuments,
            DocumentsWithClinicalSignals = clinical.SelectMany(x => x.Examples.Select(e => e.DocumentId)).Distinct().Count(),
            DocumentsWithFinancialSignals = financial.SelectMany(x => x.Examples.Select(e => e.DocumentId)).Concat(money.Select(x => x.DocumentId)).Distinct().Count(),
            CriticalAlerts = alerts.Count(a => a.Severity is "Crítico" or "Alto"),
            OcrCoveragePercent = Percent(snapshot.DocumentsWithOcr, snapshot.TotalDocuments),
            ClassificationCoveragePercent = Percent(snapshot.ClassifiedDocuments, snapshot.TotalDocuments),
            DataQualityScore = CalculateDataQuality(snapshot),
            OcrKpis = BuildOcrKpis(snapshot).ToList(),
            ClinicalTerms = clinical,
            FinancialKpis = financial,
            OperationalKpis = operational,
            Alerts = alerts,
            DocumentsByMonth = snapshot.Rows.GroupBy(r => r.CreatedAt?.ToString("yyyy-MM") ?? "Sem data").OrderBy(x => x.Key).Select(x => new TimeSeriesKpiDto { Label = x.Key, Value = x.Count() }).ToList(),
            DocumentsByFolder = BuildRanking(snapshot.Rows.Select(r => r.Folder), snapshot.Rows.Count),
            DocumentsBySector = BuildRanking(snapshot.Rows.Select(r => string.IsNullOrWhiteSpace(r.Sector) ? r.Folder : r.Sector), snapshot.Rows.Count),
            DocumentsByType = BuildRanking(snapshot.Rows.Select(r => r.DocumentType), snapshot.Rows.Count),
            Warnings = snapshot.Warnings.ToList()
        };

        if (money.Any(x => x.ParsedValue.HasValue))
            dto.Warnings.Add("Valor monetário detectado estimado. Estimativa baseada em OCR, sujeita a validação.");
        if (snapshot.Rows.Count >= top)
            dto.Warnings.Add($"Análise limitada aos {top:N0} documentos OCR mais recentes do recorte para preservar desempenho.");

        return dto;
    }

    private static IReadOnlyList<OcrKpiDto> BuildOcrKpis(HospitalOcrAnalyticsSnapshotDto s) =>
    [
        new() { Status = "Concluído", Count = s.OcrCompleted, Percentage = Percent(s.OcrCompleted, s.TotalDocuments), Description = "Documentos com texto OCR disponível." },
        new() { Status = "Pendente", Count = s.DocumentsWithoutOcr, Percentage = Percent(s.DocumentsWithoutOcr, s.TotalDocuments), Description = "Documentos sem texto OCR indexado no recorte." },
        new() { Status = "Processando", Count = s.OcrProcessing, Percentage = Percent(s.OcrProcessing, s.TotalDocuments), Description = "Documentos com processamento OCR em andamento." },
        new() { Status = "Erro", Count = s.OcrErrors, Percentage = Percent(s.OcrErrors, s.TotalDocuments), Description = "Documentos com erro registrado no OCR." },
        new() { Status = "Cancelado", Count = s.OcrCancelled, Percentage = Percent(s.OcrCancelled, s.TotalDocuments), Description = "Documentos com OCR cancelado." }
    ];

    private static IEnumerable<ClinicalTermKpiDto> BuildClinicalTerms(IReadOnlyList<TermMatchDto> terms, int documentsWithOcr)
        => terms.Where(x => IsClinicalCategory(x.Category)).Take(30).Select(x => new ClinicalTermKpiDto
        {
            Term = x.Term,
            Category = x.Category,
            Occurrences = x.Occurrences,
            DocumentCount = x.DocumentCount,
            Percentage = Percent(x.DocumentCount, documentsWithOcr),
            RiskLevel = x.RiskLevel,
            Examples = x.Examples.Select(MapSnippet).ToList()
        });

    private static IEnumerable<FinancialDocumentKpiDto> BuildFinancialKpis(IReadOnlyList<TermMatchDto> terms, IReadOnlyList<MoneySignalDto> money)
    {
        if (money.Count > 0)
        {
            yield return new FinancialDocumentKpiDto
            {
                Indicator = "Documentos com valores monetários detectados",
                DocumentCount = money.Select(x => x.DocumentId).Distinct().Count(),
                EstimatedValue = money.Where(x => x.ParsedValue.HasValue).Sum(x => x.ParsedValue!.Value),
                Description = "Valor monetário detectado estimado por OCR; não representa valor financeiro oficial sem validação.",
                RiskLevel = money.Count >= 20 ? "Alto" : "Médio",
                Examples = money.GroupBy(x => x.DocumentId).Take(3).Select(g => new AppDocumentSnippetDto
                {
                    DocumentId = g.First().DocumentId,
                    VersionId = g.First().VersionId,
                    Title = g.First().Title,
                    Folder = g.First().Folder,
                    Snippet = g.First().Snippet
                }).ToList()
            };
        }

        foreach (var term in terms.Where(x => IsFinancialCategory(x.Category)).Take(30))
        {
            yield return new FinancialDocumentKpiDto
            {
                Indicator = $"{term.Category}: {term.Term}",
                DocumentCount = term.DocumentCount,
                Description = "Sinal administrativo/financeiro detectado automaticamente em texto OCR real.",
                RiskLevel = term.RiskLevel,
                Examples = term.Examples.Select(MapSnippet).ToList()
            };
        }
    }

    private static IEnumerable<OperationalKpiDto> BuildOperationalKpis(HospitalOcrAnalyticsSnapshotDto s)
    {
        yield return new OperationalKpiDto { Indicator = "Qualidade da base digital", Value = $"{CalculateDataQuality(s):N1}%", Description = "Score ponderado por OCR concluído, classificação e ausência de erro OCR.", Recommendation = DataQualityRecommendation(s), Status = CalculateDataQuality(s) >= 80 ? "Bom" : "Atenção" };
        yield return new OperationalKpiDto { Indicator = "Pendências de OCR", Value = s.DocumentsWithoutOcr.ToString("N0"), Description = "Documentos sem texto OCR disponível no recorte.", Recommendation = "Priorizar reprocessamento dos lotes com maior volume documental.", Status = s.DocumentsWithoutOcr > 0 ? "Atenção" : "Bom" };
        yield return new OperationalKpiDto { Indicator = "Pendências de classificação", Value = s.UnclassifiedDocuments.ToString("N0"), Description = "Documentos sem classificação arquivística registrada.", Recommendation = "Aplicar regras de classificação por pasta/tipo documental e revisar filas pendentes.", Status = s.UnclassifiedDocuments > 0 ? "Atenção" : "Bom" };

        foreach (var group in s.Rows.GroupBy(r => SafeLabel(r.Folder, "Sem pasta")).OrderByDescending(g => g.Count(r => !r.IsClassified)).Take(5).Where(g => g.Any(r => !r.IsClassified)))
            yield return new OperationalKpiDto { Indicator = "Gargalo por pasta", Value = $"{group.Count(r => !r.IsClassified):N0} sem classificação em {group.Key}", Description = "Concentração de pendências de classificação por pasta.", Recommendation = "Revisar plano de classificação e metadados obrigatórios da pasta.", Status = "Atenção" };
    }

    private static IEnumerable<RiskAlertKpiDto> BuildRiskAlerts(HospitalOcrAnalyticsSnapshotDto s, IReadOnlyList<ClinicalTermKpiDto> clinical, IReadOnlyList<FinancialDocumentKpiDto> financial)
    {
        if (s.OcrErrors > 0) yield return new RiskAlertKpiDto { Title = "Erros de OCR no recorte", Description = $"Há {s.OcrErrors:N0} documentos com erro de OCR.", Severity = "Alto", Recommendation = "Reprocessar erros e avaliar qualidade dos arquivos de origem." };
        if (s.UnclassifiedDocuments > 0) yield return new RiskAlertKpiDto { Title = "Pendências de classificação", Description = $"Há {s.UnclassifiedDocuments:N0} documentos sem classificação.", Severity = s.UnclassifiedDocuments > 100 ? "Alto" : "Médio", Recommendation = "Priorizar classificação para melhorar rastreabilidade e retenção documental." };
        foreach (var term in clinical.Where(x => x.RiskLevel == "Alto").Take(5))
            yield return new RiskAlertKpiDto { Title = $"Sinal clínico relevante: {term.Term}", Description = $"{term.DocumentCount:N0} documentos citam {term.Term}.", Severity = "Alto", Recommendation = "Avaliar tendência assistencial com equipe responsável e validar amostra documental." };
        foreach (var item in financial.Where(x => x.RiskLevel == "Alto").Take(5))
            yield return new RiskAlertKpiDto { Title = item.Indicator, Description = item.Description, Severity = "Alto", Recommendation = "Encaminhar amostra para auditoria documental e validação do processo." };
    }

    private static HospitalOcrAnalyticsFilter ToAnalyticsFilter(HospitalIntelligenceFilter f) => new()
    {
        TenantId = f.TenantId,
        From = f.From,
        To = f.To?.Date.AddDays(1),
        FolderId = f.FolderId,
        Sector = f.Sector,
        DocumentType = f.DocumentType,
        Search = f.Search,
        Top = f.Top,
        RefreshCache = f.RefreshCache
    };

    private static HospitalIntelligenceFilter NormalizeFilter(HospitalIntelligenceFilter filter)
    {
        var today = DateTime.UtcNow.Date;
        var from = filter.From?.Date ?? today.AddDays(-90);
        var to = filter.To?.Date ?? today;
        if (from > to) (from, to) = (to, from);
        return new HospitalIntelligenceFilter { TenantId = filter.TenantId, From = from, To = to, FolderId = filter.FolderId, Sector = NullIfWhiteSpace(filter.Sector), DocumentType = NullIfWhiteSpace(filter.DocumentType), Search = NullIfWhiteSpace(filter.Search), Top = NormalizeTop(filter.Top), RefreshCache = filter.RefreshCache };
    }

    private static AppDocumentSnippetDto MapSnippet(AnalyticsDocumentSnippetDto x) => new() { DocumentId = x.DocumentId, VersionId = x.VersionId, Title = x.Title, Folder = x.Folder, Snippet = x.Snippet, CreatedAt = x.CreatedAt };
    private static bool IsClinicalCategory(string category) => category is "ONCOLOGIA" or "URGÊNCIA / GRAVIDADE" or "CARDIOVASCULAR" or "CRÔNICAS" or "OPERACIONAL";
    private static bool IsFinancialCategory(string category) => category is "FINANCEIRO / AUDITORIA" or "JURÍDICO / COMPLIANCE";
    private static decimal CalculateDataQuality(HospitalOcrAnalyticsSnapshotDto s) => Math.Round(Percent(s.OcrCompleted, s.TotalDocuments) * 0.45m + Percent(s.ClassifiedDocuments, s.TotalDocuments) * 0.35m + Percent(s.TotalDocuments - s.OcrErrors, s.TotalDocuments) * 0.20m, 2);
    private static string DataQualityRecommendation(HospitalOcrAnalyticsSnapshotDto s) => s.TotalDocuments == 0 ? "Não há documentos no recorte selecionado." : Percent(s.ClassifiedDocuments, s.TotalDocuments) < 70 ? "Base com rastreabilidade a melhorar: priorize classificação arquivística." : Percent(s.OcrCompleted, s.TotalDocuments) < 80 ? "Base com cobertura OCR insuficiente: priorize reprocessamento e captura textual." : "Base com boa rastreabilidade documental; manter monitoramento periódico.";
    private static List<RankingKpiDto> BuildRanking(IEnumerable<string?> values, int total) => values.Select(v => SafeLabel(v, "Não informado")).GroupBy(v => v).OrderByDescending(g => g.Count()).Take(10).Select(g => new RankingKpiDto { Label = g.Key, Value = g.Count(), Percentage = Percent(g.Count(), total) }).ToList();
    private static string SafeLabel(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int NormalizeTop(int top) => top <= 0 ? DefaultTopDocuments : Math.Min(top, MaxTopDocuments);
    private static decimal Percent(int part, int total) => total <= 0 ? 0 : Math.Round(part * 100m / total, 2);
    private static HospitalIntelligenceDashboardDto EmptyDashboard(string warning) => new() { GeneratedAt = DateTimeOffset.UtcNow, Warnings = [warning] };
}
