using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.HospitalIntelligence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.HospitalIntelligence;

public sealed class HospitalIntelligenceService : IHospitalIntelligenceService
{
    private const int DefaultTopDocuments = 1000;
    private const int MaxTopDocuments = 5000;
    private const int SnippetLength = 180;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly Regex MoneyRegex = new(@"(?:R\$\s*)?\b(?:\d{1,3}(?:\.\d{3})+|\d{1,9}),\d{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongNumberRegex = new(@"\b\d{6,}\b", RegexOptions.Compiled);

    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HospitalIntelligenceService> _logger;

    public HospitalIntelligenceService(IDbConnectionFactory db, IMemoryCache cache, ILogger<HospitalIntelligenceService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<HospitalIntelligenceDashboardDto> GetDashboardAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        var key = BuildCacheKey(f, "dashboard");
        if (!f.RefreshCache && _cache.TryGetValue(key, out HospitalIntelligenceDashboardDto? cached) && cached is not null)
            return cached;

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var schema = await LoadSchemaAsync(conn, ct);
            var counters = await LoadCountersAsync(conn, f, schema, ct);
            var baseData = await LoadBaseOcrRowsAsync(conn, f, schema, ct);

            var dto = BuildDashboard(counters, baseData, f.Top);
            if (!schema.HasDocumentSearch)
                dto.Warnings.Add("Índice textual de OCR indisponível. Indicadores de termos dependem de documentos OCR indexados.");
            if (baseData.Count >= f.Top)
                dto.Warnings.Add($"Análise limitada aos {f.Top:N0} documentos OCR mais recentes do recorte para preservar desempenho.");
            if (dto.FinancialKpis.Any(x => x.EstimatedValue.HasValue))
                dto.Warnings.Add("Valor monetário detectado estimado. Estimativa baseada em OCR, sujeita a validação.");

            _cache.Set(key, dto, CacheTtl);
            return dto;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P08")
        {
            _logger.LogError(ex, "Erro de tipagem de parâmetro SQL em HospitalIntelligence. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis por erro de consulta. Verifique os logs técnicos.");
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao gerar Inteligência Hospitalar. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Não foi possível carregar os indicadores devido a indisponibilidade temporária da base de dados.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao gerar Inteligência Hospitalar. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis no momento. Informe o código de rastreio ao suporte.");
        }
    }

    public async Task<IReadOnlyList<OcrKpiDto>> GetOcrKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => await LoadPartAsync(filter, ct, (c, r, t) => BuildOcrKpis(c));

    public async Task<IReadOnlyList<ClinicalTermKpiDto>> GetClinicalTermKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => await LoadPartAsync(filter, ct, (c, r, t) => BuildClinicalTerms(r, c.DocumentsWithOcr, t));

    public async Task<IReadOnlyList<FinancialDocumentKpiDto>> GetFinancialKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => await LoadPartAsync(filter, ct, (c, r, t) => BuildFinancialKpis(r, c.DocumentsWithOcr));

    public async Task<IReadOnlyList<OperationalKpiDto>> GetOperationalKpisAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => await LoadPartAsync(filter, ct, (c, r, t) => BuildOperationalKpis(c, r));

    public async Task<IReadOnlyList<RiskAlertKpiDto>> GetRiskAlertsAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
        => await LoadPartAsync(filter, ct, (c, r, t) => BuildRiskAlerts(c, BuildClinicalTerms(r, c.DocumentsWithOcr, t), BuildFinancialKpis(r, c.DocumentsWithOcr)));

    private async Task<IReadOnlyList<T>> LoadPartAsync<T>(HospitalIntelligenceFilter filter, CancellationToken ct, Func<CounterSnapshot, IReadOnlyList<OcrRow>, int, IReadOnlyList<T>> build)
    {
        var f = NormalizeFilter(filter);
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var schema = await LoadSchemaAsync(conn, ct);
            var counters = await LoadCountersAsync(conn, f, schema, ct);
            var rows = await LoadBaseOcrRowsAsync(conn, f, schema, ct);
            return build(counters, rows, f.Top);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P08")
        {
            _logger.LogError(ex, "Erro de tipagem de parâmetro SQL ao carregar parte da Inteligência Hospitalar. Tenant={TenantId}", f.TenantId);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar parte da Inteligência Hospitalar. Tenant={TenantId}", f.TenantId);
            return [];
        }
    }

    private async Task<SchemaSnapshot> LoadSchemaAsync(IDbConnection conn, CancellationToken ct)
    {
        const string sql = @"
select
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='document_search') as ""HasDocumentSearch"",
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='document_version') as ""HasDocumentVersion"",
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='ocr_job') as ""HasOcrJob"",
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='preview_status') as ""HasPreviewStatus"";";
        return await conn.QuerySingleAsync<SchemaSnapshot>(new CommandDefinition(sql, cancellationToken: ct));
    }

    private async Task<CounterSnapshot> LoadCountersAsync(IDbConnection conn, HospitalIntelligenceFilter f, SchemaSnapshot schema, CancellationToken ct)
    {
        _logger.LogInformation(
            "Carregando counters HospitalIntelligence. Tenant={TenantId} From={From} To={To} FolderId={FolderId}",
            f.TenantId,
            f.From,
            f.To,
            f.FolderId);

        var parameters = BuildSqlParams(f);
        var ocrJobJoin = schema.HasOcrJob ? "left join ged.ocr_job oj on oj.tenant_id = d.tenant_id and oj.document_version_id = d.current_version_id" : "";
        var previewJoin = schema.HasPreviewStatus ? "left join ged.preview_status ps on ps.tenant_id = d.tenant_id and ps.document_version_id = d.current_version_id" : "";
        var ocrStatusExpr = schema.HasOcrJob ? "upper(coalesce(oj.status::text, ''))" : "''";
        var hasOcrExpr = schema.HasDocumentSearch ? "exists(select 1 from ged.document_search ds where ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> '')" : "false";
        var searchExpr = schema.HasDocumentSearch ? "or exists(select 1 from ged.document_search dsf where dsf.tenant_id=d.tenant_id and dsf.document_id=d.id and dsf.ocr_text ilike '%' || @Search::text || '%')" : "";
        var previewExpr = schema.HasPreviewStatus ? "ps.preview_path is not null" : "false";
        var sql = $@"
with filtered as (
 select d.id, d.created_at, d.folder_id, coalesce(fol.name,'Sem pasta') folder_name, coalesce(dt.name,'Sem tipo') document_type,
        coalesce(d.classification_id, d.classification_version_id) classification_id,
        {hasOcrExpr} has_ocr,
        {previewExpr} has_preview,
        {ocrStatusExpr} ocr_status
 from ged.document d
 left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
 left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
 {ocrJobJoin}
 {previewJoin}
 where d.tenant_id=@TenantId::uuid and coalesce(d.reg_status,'A')='A'
   and d.created_at >= @From::timestamp and d.created_at < @ToExclusive::timestamp
   and (@FolderId::uuid is null or d.folder_id=@FolderId::uuid)
   and (@Sector::text is null or fol.name ilike '%' || @Sector::text || '%')
   and (@DocumentType::text is null or dt.name ilike '%' || @DocumentType::text || '%')
   and (@Search::text is null or d.title ilike '%' || @Search::text || '%' {searchExpr})
)
select count(distinct id)::int TotalDocuments,
       count(distinct id) filter (where has_ocr)::int DocumentsWithOcr,
       count(distinct id) filter (where not has_ocr)::int DocumentsWithoutOcr,
       count(distinct id) filter (where ocr_status in ('PENDING','QUEUED'))::int OcrPending,
       count(distinct id) filter (where ocr_status='PROCESSING')::int OcrProcessing,
       count(distinct id) filter (where has_ocr or ocr_status='COMPLETED')::int OcrCompleted,
       count(distinct id) filter (where ocr_status in ('ERROR','FAILED'))::int OcrErrors,
       count(distinct id) filter (where ocr_status='CANCELLED')::int OcrCancelled,
       count(distinct id) filter (where classification_id is null)::int UnclassifiedDocuments,
       count(distinct id) filter (where classification_id is not null)::int ClassifiedDocuments,
       count(distinct id) filter (where has_preview)::int DocumentsWithPreview,
       count(distinct id) filter (where folder_id is not null)::int DocumentsWithFolder
from filtered;";
        return await conn.QuerySingleAsync<CounterSnapshot>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    private async Task<List<OcrRow>> LoadBaseOcrRowsAsync(IDbConnection conn, HospitalIntelligenceFilter f, SchemaSnapshot schema, CancellationToken ct)
    {
        if (!schema.HasDocumentSearch)
            return [];

        _logger.LogInformation(
            "Carregando linhas OCR HospitalIntelligence. Tenant={TenantId} From={From} To={To} FolderId={FolderId}",
            f.TenantId,
            f.From,
            f.To,
            f.FolderId);

        var sql = @"
select d.id as ""DocumentId"", ds.version_id as ""VersionId"", coalesce(d.title, ds.title, 'Sem título') as ""Title"",
       coalesce(fol.name, 'Sem pasta') as ""Folder"", coalesce(dt.name, 'Sem tipo') as ""DocumentType"",
       coalesce(fol.name, '') as ""Sector"", d.created_at as ""CreatedAt"", coalesce(d.classification_id, d.classification_version_id) is not null as ""IsClassified"",
       substring(ds.ocr_text from 1 for 12000) as ""Text""
from ged.document d
join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> ''
left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
where d.tenant_id=@TenantId::uuid and coalesce(d.reg_status,'A')='A'
  and d.created_at >= @From::timestamp and d.created_at < @ToExclusive::timestamp
  and (@FolderId::uuid is null or d.folder_id=@FolderId::uuid)
  and (@Sector::text is null or fol.name ilike '%' || @Sector::text || '%')
  and (@DocumentType::text is null or dt.name ilike '%' || @DocumentType::text || '%')
  and (@Search::text is null or ds.ocr_text ilike '%' || @Search::text || '%' or d.title ilike '%' || @Search::text || '%')
order by d.created_at desc
limit @Top::int;";
        var rows = await conn.QueryAsync<OcrRow>(new CommandDefinition(sql, BuildSqlParams(f), cancellationToken: ct));
        var list = rows.ToList();
        foreach (var row in list) row.Text = MaskSensitive(row.Text ?? string.Empty);
        return list;
    }

    private static HospitalIntelligenceDashboardDto BuildDashboard(CounterSnapshot c, IReadOnlyList<OcrRow> rows, int top)
    {
        var clinical = BuildClinicalTerms(rows, c.DocumentsWithOcr, top);
        var financial = BuildFinancialKpis(rows, c.DocumentsWithOcr);
        var operational = BuildOperationalKpis(c, rows);
        var alerts = BuildRiskAlerts(c, clinical, financial);

        return new HospitalIntelligenceDashboardDto
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            TotalDocuments = c.TotalDocuments,
            DocumentsWithOcr = c.DocumentsWithOcr,
            DocumentsWithoutOcr = c.DocumentsWithoutOcr,
            OcrPending = c.OcrPending > 0 ? c.OcrPending : c.DocumentsWithoutOcr,
            OcrProcessing = c.OcrProcessing,
            OcrCompleted = c.OcrCompleted,
            OcrErrors = c.OcrErrors,
            OcrCancelled = c.OcrCancelled,
            UnclassifiedDocuments = c.UnclassifiedDocuments,
            ClassifiedDocuments = c.ClassifiedDocuments,
            DocumentsWithClinicalSignals = clinical.SelectMany(x => x.Examples.Select(e => e.DocumentId)).Distinct().Count(),
            DocumentsWithFinancialSignals = financial.SelectMany(x => x.Examples.Select(e => e.DocumentId)).Distinct().Count(),
            CriticalAlerts = alerts.Count(a => a.Severity is "Crítico" or "Alto"),
            OcrCoveragePercent = Percent(c.DocumentsWithOcr, c.TotalDocuments),
            ClassificationCoveragePercent = Percent(c.ClassifiedDocuments, c.TotalDocuments),
            DataQualityScore = CalculateDataQuality(c),
            OcrKpis = BuildOcrKpis(c).ToList(),
            ClinicalTerms = clinical.ToList(),
            FinancialKpis = financial.ToList(),
            OperationalKpis = operational.ToList(),
            Alerts = alerts.ToList(),
            DocumentsByMonth = rows.GroupBy(r => r.CreatedAt?.ToString("yyyy-MM") ?? "Sem data").OrderBy(x => x.Key).Select(x => new TimeSeriesKpiDto { Label = x.Key, Value = x.Count() }).ToList(),
            DocumentsByFolder = BuildRanking(rows.Select(r => r.Folder), rows.Count),
            DocumentsBySector = BuildRanking(rows.Select(r => string.IsNullOrWhiteSpace(r.Sector) ? r.Folder : r.Sector), rows.Count),
            DocumentsByType = BuildRanking(rows.Select(r => r.DocumentType), rows.Count)
        };
    }

    private static IReadOnlyList<OcrKpiDto> BuildOcrKpis(CounterSnapshot c) =>
    [
        new() { Status = "Concluído", Count = c.OcrCompleted, Percentage = Percent(c.OcrCompleted, c.TotalDocuments), Description = "Documentos com texto OCR disponível." },
        new() { Status = "Pendente", Count = c.DocumentsWithoutOcr, Percentage = Percent(c.DocumentsWithoutOcr, c.TotalDocuments), Description = "Documentos sem texto OCR indexado no recorte." },
        new() { Status = "Processando", Count = c.OcrProcessing, Percentage = Percent(c.OcrProcessing, c.TotalDocuments), Description = "Documentos com processamento OCR em andamento." },
        new() { Status = "Erro", Count = c.OcrErrors, Percentage = Percent(c.OcrErrors, c.TotalDocuments), Description = "Documentos com erro registrado no OCR." },
        new() { Status = "Cancelado", Count = c.OcrCancelled, Percentage = Percent(c.OcrCancelled, c.TotalDocuments), Description = "Documentos com OCR cancelado." }
    ];

    private static IReadOnlyList<ClinicalTermKpiDto> BuildClinicalTerms(IReadOnlyList<OcrRow> rows, int documentsWithOcr, int top)
    {
        var map = new Dictionary<string, string[]>
        {
            ["ONCOLOGIA"] = ["câncer", "neoplasia", "tumor", "carcinoma", "linfoma", "leucemia", "metástase", "oncologia", "quimioterapia", "radioterapia", "biópsia"],
            ["URGÊNCIA / GRAVIDADE"] = ["sepse", "UTI", "intubação", "parada cardiorrespiratória", "choque", "hemorragia", "trauma", "óbito", "emergência", "urgência"],
            ["CARDIOVASCULAR"] = ["infarto", "IAM", "AVC", "hipertensão", "insuficiência cardíaca", "arritmia", "dor torácica"],
            ["CRÔNICAS"] = ["diabetes", "renal crônico", "DPOC", "hemodiálise", "hipertensão"],
            ["PROCESSOS ASSISTENCIAIS"] = ["cirurgia", "internação", "alta", "transferência", "regulação", "laudo", "prescrição", "exame", "tomografia", "ultrassom", "ressonância"]
        };

        return BuildTermKpis(rows, documentsWithOcr, map, top)
            .Select(x => new ClinicalTermKpiDto { Term = x.Term, Category = x.Category, Occurrences = x.Occurrences, DocumentCount = x.DocumentCount, Percentage = x.Percentage, RiskLevel = x.RiskLevel, Examples = x.Examples })
            .ToList();
    }

    private static IReadOnlyList<FinancialDocumentKpiDto> BuildFinancialKpis(IReadOnlyList<OcrRow> rows, int documentsWithOcr)
    {
        var map = new Dictionary<string, string[]>
        {
            ["FINANCEIRO"] = ["nota fiscal", "contrato", "pagamento", "empenho", "orçamento", "compra", "cobrança", "faturamento"],
            ["AUDITORIA / RECEITA"] = ["glosa", "conta hospitalar", "autorização", "convênio", "SUS", "auditoria", "recurso", "procedimento", "diária", "OPME", "medicamento", "material"],
            ["JURÍDICO / COMPLIANCE"] = ["processo", "ofício", "parecer", "notificação", "judicial", "mandado", "determinação", "sindicância"]
        };

        var list = BuildTermKpis(rows, documentsWithOcr, map, 30)
            .Select(x => new FinancialDocumentKpiDto
            {
                Indicator = $"{x.Category}: {x.Term}",
                DocumentCount = x.DocumentCount,
                Description = "Sinal administrativo/financeiro detectado automaticamente em texto OCR real.",
                RiskLevel = x.RiskLevel,
                Examples = x.Examples
            }).ToList();

        var moneyDocs = rows.Where(r => MoneyRegex.IsMatch(r.Text ?? string.Empty)).ToList();
        if (moneyDocs.Count > 0)
        {
            list.Insert(0, new FinancialDocumentKpiDto
            {
                Indicator = "Documentos com valores monetários detectados",
                DocumentCount = moneyDocs.Count,
                EstimatedValue = moneyDocs.SelectMany(r => MoneyRegex.Matches(r.Text ?? string.Empty).Select(m => TryParseMoney(m.Value))).Where(v => v.HasValue).Sum(v => v!.Value),
                Description = "Valor monetário detectado estimado por OCR; não representa valor financeiro oficial sem validação.",
                RiskLevel = moneyDocs.Count >= 20 ? "Alto" : "Médio",
                Examples = moneyDocs.Take(3).Select(r => ToSnippet(r, MoneyRegex.Match(r.Text ?? string.Empty).Value)).ToList()
            });
        }

        return list;
    }

    private static IReadOnlyList<OperationalKpiDto> BuildOperationalKpis(CounterSnapshot c, IReadOnlyList<OcrRow> rows)
    {
        var list = new List<OperationalKpiDto>
        {
            new() { Indicator = "Qualidade da base digital", Value = $"{CalculateDataQuality(c):N1}%", Description = "Score ponderado por OCR concluído, classificação, preview, ausência de erro OCR e pasta válida.", Recommendation = DataQualityRecommendation(c), Status = CalculateDataQuality(c) >= 80 ? "Bom" : "Atenção" },
            new() { Indicator = "Pendências de OCR", Value = c.DocumentsWithoutOcr.ToString("N0"), Description = "Documentos sem texto OCR disponível no recorte.", Recommendation = "Priorizar reprocessamento dos lotes com maior volume documental.", Status = c.DocumentsWithoutOcr > 0 ? "Atenção" : "Bom" },
            new() { Indicator = "Pendências de classificação", Value = c.UnclassifiedDocuments.ToString("N0"), Description = "Documentos sem classificação arquivística registrada.", Recommendation = "Aplicar regras de classificação por pasta/tipo documental e revisar filas pendentes.", Status = c.UnclassifiedDocuments > 0 ? "Atenção" : "Bom" }
        };

        list.AddRange(rows.GroupBy(r => r.Folder).OrderByDescending(g => g.Count(r => !r.IsClassified)).Take(5)
            .Where(g => g.Any(r => !r.IsClassified))
            .Select(g => new OperationalKpiDto { Indicator = "Gargalo por pasta", Value = $"{g.Count(r => !r.IsClassified):N0} sem classificação em {g.Key}", Description = "Concentração de pendências de classificação por pasta.", Recommendation = "Revisar plano de classificação e metadados obrigatórios da pasta.", Status = "Atenção" }));

        list.AddRange(rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Sector) ? r.Folder : r.Sector).OrderByDescending(g => g.Count()).Take(5)
            .Select(g => new OperationalKpiDto { Indicator = "Volume documental por setor", Value = $"{g.Count():N0} em {g.Key}", Description = "Setor/pasta com maior volume de documentos OCR analisados.", Recommendation = "Dimensionar filas, conferência e automações conforme concentração documental.", Status = "Informativo" }));

        return list;
    }

    private static IReadOnlyList<RiskAlertKpiDto> BuildRiskAlerts(CounterSnapshot c, IReadOnlyList<ClinicalTermKpiDto> clinical, IReadOnlyList<FinancialDocumentKpiDto> financial)
    {
        var alerts = new List<RiskAlertKpiDto>();
        if (c.OcrErrors > 0) alerts.Add(new() { Title = "Erros de OCR no recorte", Description = $"Há {c.OcrErrors:N0} documentos com erro de OCR.", Severity = "Alto", Recommendation = "Reprocessar erros e avaliar qualidade dos arquivos de origem." });
        if (c.UnclassifiedDocuments > 0) alerts.Add(new() { Title = "Pendências de classificação", Description = $"Há {c.UnclassifiedDocuments:N0} documentos sem classificação.", Severity = c.UnclassifiedDocuments > 100 ? "Alto" : "Médio", Recommendation = "Priorizar classificação para melhorar rastreabilidade e retenção documental." });
        foreach (var term in clinical.Where(t => t.RiskLevel is "Alto" or "Crítico").Take(5))
            alerts.Add(new() { Title = $"Termo crítico recorrente: {term.Term}", Description = $"Detectado em {term.DocumentCount:N0} documentos OCR da categoria {term.Category}.", Severity = term.RiskLevel, Recommendation = "Usar apenas como indicador documental/epidemiológico preliminar e validar com áreas responsáveis." });
        foreach (var item in financial.Where(f => f.RiskLevel is "Alto").Take(3))
            alerts.Add(new() { Title = item.Indicator, Description = $"Sinal administrativo/financeiro em {item.DocumentCount:N0} documentos.", Severity = item.RiskLevel, Recommendation = "Submeter amostra documental à auditoria administrativa/financeira." });
        return alerts;
    }

    private static IReadOnlyList<TermKpi> BuildTermKpis(IReadOnlyList<OcrRow> rows, int documentsWithOcr, IReadOnlyDictionary<string, string[]> map, int top)
    {
        var result = new List<TermKpi>();
        foreach (var (category, terms) in map)
        {
            foreach (var term in terms.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var normalizedTerm = Normalize(term);
                var matched = rows.Select(r => new { Row = r, Count = CountOccurrences(Normalize(r.Text ?? string.Empty), normalizedTerm) }).Where(x => x.Count > 0).ToList();
                if (matched.Count == 0) continue;
                var occurrences = matched.Sum(x => x.Count);
                result.Add(new TermKpi(term, category, occurrences, matched.Count, Percent(matched.Count, documentsWithOcr), RiskLevel(category, matched.Count, occurrences), matched.Take(3).Select(x => ToSnippet(x.Row, term)).ToList()));
            }
        }
        return result.OrderByDescending(x => x.DocumentCount).ThenByDescending(x => x.Occurrences).Take(top <= 0 ? 20 : top).ToList();
    }

    private static DocumentSnippetDto ToSnippet(OcrRow row, string term) => new()
    {
        DocumentId = row.DocumentId,
        VersionId = row.VersionId == Guid.Empty ? null : row.VersionId,
        Title = MaskTitle(row.Title),
        Folder = row.Folder,
        CreatedAt = row.CreatedAt,
        Snippet = ExtractSnippet(row.Text ?? string.Empty, term)
    };

    private static string ExtractSnippet(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Trecho OCR indisponível no recorte.";
        var idx = CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        if (idx < 0) idx = 0;
        var start = Math.Max(0, idx - 70);
        var len = Math.Min(SnippetLength, text.Length - start);
        var snippet = text.Substring(start, len).ReplaceLineEndings(" ");
        return MaskSensitive((start > 0 ? "..." : "") + snippet + (start + len < text.Length ? "..." : ""));
    }

    private static decimal CalculateDataQuality(CounterSnapshot c)
        => Math.Round(Percent(c.OcrCompleted, c.TotalDocuments) * 0.30m + Percent(c.ClassifiedDocuments, c.TotalDocuments) * 0.25m + Percent(c.DocumentsWithPreview, c.TotalDocuments) * 0.15m + Percent(c.TotalDocuments - c.OcrErrors, c.TotalDocuments) * 0.15m + Percent(c.DocumentsWithFolder, c.TotalDocuments) * 0.15m, 2);

    private static string DataQualityRecommendation(CounterSnapshot c)
    {
        if (c.TotalDocuments == 0) return "Não há documentos no recorte selecionado.";
        if (Percent(c.ClassifiedDocuments, c.TotalDocuments) < 70) return "Base com rastreabilidade a melhorar: priorize classificação arquivística.";
        if (Percent(c.OcrCompleted, c.TotalDocuments) < 80) return "Base com cobertura OCR insuficiente: priorize reprocessamento e captura textual.";
        return "Base com boa rastreabilidade documental; manter monitoramento periódico.";
    }

    private static List<RankingKpiDto> BuildRanking(IEnumerable<string?> values, int total)
        => values.Select(v => string.IsNullOrWhiteSpace(v) ? "Não informado" : v.Trim()).GroupBy(v => v).OrderByDescending(g => g.Count()).Take(10).Select(g => new RankingKpiDto { Label = g.Key, Value = g.Count(), Percentage = Percent(g.Count(), total) }).ToList();

    private static DynamicParameters BuildSqlParams(HospitalIntelligenceFilter f)
    {
        var p = new DynamicParameters();
        p.Add("TenantId", f.TenantId, DbType.Guid);
        p.Add("From", f.From!.Value, DbType.DateTime);
        p.Add("ToExclusive", f.To!.Value.Date.AddDays(1), DbType.DateTime);
        p.Add("FolderId", f.FolderId, DbType.Guid);
        p.Add("Sector", NullIfWhiteSpace(f.Sector), DbType.String);
        p.Add("DocumentType", NullIfWhiteSpace(f.DocumentType), DbType.String);
        p.Add("Search", NullIfWhiteSpace(f.Search), DbType.String);
        p.Add("Top", NormalizeTop(f.Top), DbType.Int32);
        return p;
    }

    private static HospitalIntelligenceFilter NormalizeFilter(HospitalIntelligenceFilter filter)
    {
        var today = DateTime.UtcNow.Date;
        var from = filter.From?.Date ?? today.AddDays(-90);
        var to = filter.To?.Date ?? today;
        if (from > to) (from, to) = (to, from);
        return new HospitalIntelligenceFilter { TenantId = filter.TenantId, From = from, To = to, FolderId = filter.FolderId, Sector = NullIfWhiteSpace(filter.Sector), DocumentType = NullIfWhiteSpace(filter.DocumentType), Search = NullIfWhiteSpace(filter.Search), Top = NormalizeTop(filter.Top), RefreshCache = filter.RefreshCache };
    }

    private static string BuildCacheKey(HospitalIntelligenceFilter f, string part) => $"HospitalIntelligence:{part}:{f.TenantId}:{f.From:yyyyMMdd}:{f.To:yyyyMMdd}:{f.FolderId}:{f.Sector}:{f.DocumentType}:{f.Search}:{f.Top}";
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int NormalizeTop(int top) => top <= 0 ? DefaultTopDocuments : Math.Min(top, MaxTopDocuments);
    private static decimal Percent(int part, int total) => total <= 0 ? 0 : Math.Round(part * 100m / total, 2);
    private static string RiskLevel(string category, int docs, int occurrences) => category.Contains("URGÊNCIA", StringComparison.OrdinalIgnoreCase) || docs >= 20 || occurrences >= 50 ? "Alto" : docs >= 5 ? "Médio" : "Baixo";
    private static int CountOccurrences(string text, string term) => string.IsNullOrWhiteSpace(term) ? 0 : Regex.Matches(text, $@"(?<!\p{{L}}){Regex.Escape(term)}(?!\p{{L}})", RegexOptions.IgnoreCase).Count;
    private static string Normalize(string text) => string.Concat((text ?? string.Empty).Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Normalize(NormalizationForm.FormC).ToLowerInvariant();
    private static string MaskSensitive(string text) => LongNumberRegex.Replace(EmailRegex.Replace(CpfRegex.Replace(text, "***.***.***-**"), "***@***"), "******");
    private static string MaskTitle(string title) => MaskSensitive(title ?? "Sem título");
    private static decimal? TryParseMoney(string value) => decimal.TryParse(value.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(".", ""), NumberStyles.Number, new CultureInfo("pt-BR"), out var parsed) ? parsed : null;

    private static HospitalIntelligenceDashboardDto EmptyDashboard(string warning) => new() { GeneratedAt = DateTimeOffset.UtcNow, Warnings = [warning] };

    private sealed class SchemaSnapshot
    {
        public bool HasDocumentSearch { get; set; }
        public bool HasDocumentVersion { get; set; }
        public bool HasOcrJob { get; set; }
        public bool HasPreviewStatus { get; set; }
    }

    private sealed class CounterSnapshot
    {
        public int TotalDocuments { get; set; }
        public int DocumentsWithOcr { get; set; }
        public int DocumentsWithoutOcr { get; set; }
        public int OcrPending { get; set; }
        public int OcrProcessing { get; set; }
        public int OcrCompleted { get; set; }
        public int OcrErrors { get; set; }
        public int OcrCancelled { get; set; }
        public int UnclassifiedDocuments { get; set; }
        public int ClassifiedDocuments { get; set; }
        public int DocumentsWithPreview { get; set; }
        public int DocumentsWithFolder { get; set; }
    }

    private sealed class OcrRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public bool IsClassified { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    private sealed record TermKpi(string Term, string Category, int Occurrences, int DocumentCount, decimal Percentage, string RiskLevel, List<DocumentSnippetDto> Examples);
}
