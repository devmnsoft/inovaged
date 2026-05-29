using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.HospitalTrends;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.HospitalTrends;

public sealed class HospitalTrendsService : IHospitalTrendsService
{
    private const int DefaultTopDocuments = 1000;
    private const int MaxTopDocuments = 5000;
    private const int SnippetLength = 200;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongNumberRegex = new(@"\b\d{6,}\b", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string[]> TermCategories = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["ONCOLOGIA"] = ["câncer", "neoplasia", "tumor", "carcinoma", "linfoma", "leucemia", "metástase", "oncologia", "quimioterapia", "radioterapia", "biópsia"],
        ["URGÊNCIA / GRAVIDADE"] = ["sepse", "UTI", "intubação", "choque", "hemorragia", "trauma", "óbito", "parada cardiorrespiratória", "emergência", "urgência"],
        ["CARDIOVASCULAR"] = ["infarto", "IAM", "AVC", "hipertensão", "insuficiência cardíaca", "arritmia", "dor torácica"],
        ["CRÔNICAS"] = ["diabetes", "doença renal crônica", "hemodiálise", "DPOC", "hipertensão"],
        ["FINANCEIRO / AUDITORIA"] = ["glosa", "nota fiscal", "faturamento", "contrato", "pagamento", "empenho", "conta hospitalar", "convênio", "SUS", "autorização", "auditoria", "cobrança", "OPME", "medicamento", "material"],
        ["JURÍDICO / COMPLIANCE"] = ["processo", "judicial", "ofício", "parecer", "notificação", "sindicância", "mandado", "determinação", "denúncia"],
        ["OPERACIONAL"] = ["internação", "alta", "transferência", "regulação", "laudo", "prescrição", "exame", "tomografia", "ultrassom", "ressonância", "cirurgia"]
    };

    private static readonly string[] CriticalTerms = ["óbito", "sepse", "UTI"];

    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HospitalTrendsService> _logger;

    public HospitalTrendsService(IDbConnectionFactory db, IMemoryCache cache, ILogger<HospitalTrendsService> logger)
    {
        _db = db;
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
            await using var conn = await _db.OpenAsync(ct);
            var schema = await LoadSchemaAsync(conn, ct);
            var currentCounters = await LoadCountersAsync(conn, f, f.From!.Value, f.To!.Value, schema, ct);
            var previousCounters = await LoadCountersAsync(conn, f, f.CompareFrom!.Value, f.CompareTo!.Value, schema, ct);
            var currentRows = await LoadDocumentRowsAsync(conn, f, f.From.Value, f.To.Value, schema, ct);
            var previousRows = await LoadDocumentRowsAsync(conn, f, f.CompareFrom.Value, f.CompareTo.Value, schema, ct);
            var currentOcr = await LoadOcrRowsAsync(conn, f, f.From.Value, f.To.Value, schema, ct);
            var previousOcr = await LoadOcrRowsAsync(conn, f, f.CompareFrom.Value, f.CompareTo.Value, schema, ct);

            var termTrends = BuildTermTrends(currentOcr, previousOcr, f).ToList();
            var sectorTrends = BuildSectorTrends(currentRows, previousRows, f.Top).ToList();
            var operational = BuildOperationalTrends(currentCounters, previousCounters).ToList();
            var alerts = BuildAlerts(termTrends, sectorTrends, operational, currentCounters).ToList();
            var warnings = BuildWarnings(schema, currentOcr.Count, previousOcr.Count, f.Top).ToList();

            var dashboard = new HospitalTrendsDashboardDto
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                PeriodLabel = BuildPeriodLabel(f.From.Value, f.To.Value),
                ComparePeriodLabel = BuildPeriodLabel(f.CompareFrom.Value, f.CompareTo.Value),
                TotalDocumentsCurrent = currentCounters.TotalDocuments,
                TotalDocumentsPrevious = previousCounters.TotalDocuments,
                VariationPercent = Variation(currentCounters.TotalDocuments, previousCounters.TotalDocuments),
                TermTrends = termTrends,
                Alerts = alerts,
                SectorTrends = sectorTrends,
                OperationalTrends = operational,
                TopFolders = BuildRanking(currentRows.Select(x => x.Folder), currentRows.Count),
                TopDocumentTypes = BuildRanking(currentRows.Select(x => x.DocumentType), currentRows.Count),
                Warnings = warnings
            };
            dashboard.TotalAlerts = alerts.Count;
            dashboard.CriticalAlerts = alerts.Count(a => a.Severity is "Alto" or "Crítico");
            dashboard.WarningAlerts = alerts.Count(a => a.Severity is "Médio" or "Médio/Alto");

            _cache.Set(cacheKey, dashboard, CacheTtl);
            return dashboard;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao gerar Central de Alertas e Tendências Hospitalares. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Não foi possível consultar a base documental real no momento.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao gerar Central de Alertas e Tendências Hospitalares. Tenant={TenantId}", f.TenantId);
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

    private async Task<SchemaSnapshot> LoadSchemaAsync(IDbConnection conn, CancellationToken ct)
    {
        const string sql = @"
select
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='document_search') as ""HasDocumentSearch"",
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='ocr_job') as ""HasOcrJob"";";
        return await conn.QuerySingleAsync<SchemaSnapshot>(new CommandDefinition(sql, cancellationToken: ct));
    }

    private async Task<CounterSnapshot> LoadCountersAsync(IDbConnection conn, HospitalTrendsFilter f, DateTime from, DateTime to, SchemaSnapshot schema, CancellationToken ct)
    {
        var ocrJobJoin = schema.HasOcrJob ? "left join ged.ocr_job oj on oj.tenant_id=d.tenant_id and oj.document_version_id=d.current_version_id" : string.Empty;
        var ocrStatusExpr = schema.HasOcrJob ? "upper(coalesce(oj.status::text, ''))" : "''";
        var hasOcrExpr = schema.HasDocumentSearch ? "exists(select 1 from ged.document_search ds where ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> '')" : "false";
        var sql = $@"
with filtered as (
 select d.id,
        coalesce(d.classification_id, d.classification_version_id) classification_id,
        {hasOcrExpr} has_ocr,
        {ocrStatusExpr} ocr_status
 from ged.document d
 left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
 left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
 {ocrJobJoin}
 where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A'
   and d.created_at >= @From and d.created_at < @ToExclusive
   and (@FolderId is null or d.folder_id=@FolderId)
   and (@Sector is null or fol.name ilike '%' || @Sector || '%')
   and (@DocumentType is null or dt.name ilike '%' || @DocumentType || '%')
)
select count(distinct id)::int as ""TotalDocuments"",
       count(distinct id) filter (where has_ocr)::int as ""DocumentsWithOcr"",
       count(distinct id) filter (where not has_ocr or ocr_status in ('PENDING','QUEUED'))::int as ""PendingOcr"",
       count(distinct id) filter (where ocr_status in ('ERROR','FAILED'))::int as ""OcrErrors"",
       count(distinct id) filter (where classification_id is null)::int as ""Unclassified""
from filtered;";
        return await conn.QuerySingleAsync<CounterSnapshot>(new CommandDefinition(sql, BuildSqlParams(f, from, to), cancellationToken: ct));
    }

    private async Task<List<DocumentRow>> LoadDocumentRowsAsync(IDbConnection conn, HospitalTrendsFilter f, DateTime from, DateTime to, SchemaSnapshot schema, CancellationToken ct)
    {
        var ocrJobJoin = schema.HasOcrJob ? "left join ged.ocr_job oj on oj.tenant_id=d.tenant_id and oj.document_version_id=d.current_version_id" : string.Empty;
        var ocrStatusExpr = schema.HasOcrJob ? "upper(coalesce(oj.status::text, ''))" : "''";
        var hasOcrExpr = schema.HasDocumentSearch ? "exists(select 1 from ged.document_search ds where ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> '')" : "false";
        var sql = $@"
select distinct on (d.id) d.id as ""DocumentId"",
       coalesce(fol.name, 'Sem pasta') as ""Folder"",
       coalesce(dt.name, 'Sem tipo') as ""DocumentType"",
       coalesce(fol.name, 'Não informado') as ""Sector"",
       d.created_at as ""CreatedAt"",
       coalesce(d.classification_id, d.classification_version_id) is not null as ""IsClassified"",
       {hasOcrExpr} as ""HasOcr"",
       {ocrStatusExpr} as ""OcrStatus""
from ged.document d
left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
{ocrJobJoin}
where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A'
  and d.created_at >= @From and d.created_at < @ToExclusive
  and (@FolderId is null or d.folder_id=@FolderId)
  and (@Sector is null or fol.name ilike '%' || @Sector || '%')
  and (@DocumentType is null or dt.name ilike '%' || @DocumentType || '%')
order by d.id, d.created_at desc
limit @Top;";
        var rows = await conn.QueryAsync<DocumentRow>(new CommandDefinition(sql, BuildSqlParams(f, from, to), cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<List<OcrRow>> LoadOcrRowsAsync(IDbConnection conn, HospitalTrendsFilter f, DateTime from, DateTime to, SchemaSnapshot schema, CancellationToken ct)
    {
        if (!schema.HasDocumentSearch)
            return [];

        var sql = @"
select distinct on (d.id) d.id as ""DocumentId"", ds.version_id as ""VersionId"",
       coalesce(d.title, ds.title, 'Sem título') as ""Title"",
       coalesce(fol.name, 'Sem pasta') as ""Folder"",
       coalesce(dt.name, 'Sem tipo') as ""DocumentType"",
       coalesce(fol.name, 'Não informado') as ""Sector"",
       d.created_at as ""CreatedAt"",
       substring(ds.ocr_text from 1 for 12000) as ""Text""
from ged.document d
join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> ''
left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A'
  and d.created_at >= @From and d.created_at < @ToExclusive
  and (@FolderId is null or d.folder_id=@FolderId)
  and (@Sector is null or fol.name ilike '%' || @Sector || '%')
  and (@DocumentType is null or dt.name ilike '%' || @DocumentType || '%')
order by d.id, d.created_at desc
limit @Top;";
        var rows = (await conn.QueryAsync<OcrRow>(new CommandDefinition(sql, BuildSqlParams(f, from, to), cancellationToken: ct))).ToList();
        foreach (var row in rows)
        {
            row.Text = MaskSensitive(row.Text ?? string.Empty);
            row.Title = MaskSensitive(row.Title ?? string.Empty);
        }
        return rows;
    }

    private static IReadOnlyList<TrendKpiDto> BuildTermTrends(IReadOnlyList<OcrRow> currentRows, IReadOnlyList<OcrRow> previousRows, HospitalTrendsFilter filter)
    {
        var categories = TermCategories
            .Where(c => string.IsNullOrWhiteSpace(filter.Category) || c.Key.Equals(filter.Category, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var trends = new List<TrendKpiDto>();

        foreach (var (category, terms) in categories)
        {
            foreach (var term in terms.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var normalizedTerm = Normalize(term);
                var currentMatches = currentRows.Where(r => CountOccurrences(Normalize(r.Text), normalizedTerm) > 0).ToList();
                var previousCount = previousRows.Count(r => CountOccurrences(Normalize(r.Text), normalizedTerm) > 0);
                if (currentMatches.Count == 0 && previousCount == 0)
                    continue;

                var variation = Variation(currentMatches.Count, previousCount);
                var direction = Direction(currentMatches.Count, previousCount, variation);
                var risk = RiskLevel(term, category, currentMatches.Count, variation, direction);
                trends.Add(new TrendKpiDto
                {
                    Term = term,
                    Category = category,
                    CurrentCount = currentMatches.Count,
                    PreviousCount = previousCount,
                    VariationPercent = variation,
                    TrendDirection = direction,
                    RiskLevel = risk,
                    Interpretation = BuildTermInterpretation(term, currentMatches.Count, previousCount, variation, direction),
                    Examples = currentMatches.Take(3).Select(r => ToSnippet(r, term)).ToList()
                });
            }
        }

        return trends
            .OrderByDescending(t => t.RiskLevel is "Alto" or "Crítico")
            .ThenByDescending(t => t.VariationPercent)
            .ThenByDescending(t => t.CurrentCount)
            .Take(filter.Top <= 0 ? DefaultTopDocuments : Math.Min(filter.Top, MaxTopDocuments))
            .ToList();
    }

    private static IReadOnlyList<SectorTrendDto> BuildSectorTrends(IReadOnlyList<DocumentRow> currentRows, IReadOnlyList<DocumentRow> previousRows, int top)
    {
        var sectors = currentRows.Select(r => r.Sector).Concat(previousRows.Select(r => r.Sector)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase);
        return sectors.Select(sector =>
        {
            var current = currentRows.Where(r => r.Sector.Equals(sector, StringComparison.OrdinalIgnoreCase)).ToList();
            var previous = previousRows.Count(r => r.Sector.Equals(sector, StringComparison.OrdinalIgnoreCase));
            var variation = Variation(current.Count, previous);
            return new SectorTrendDto
            {
                Sector = sector,
                CurrentDocuments = current.Count,
                PreviousDocuments = previous,
                VariationPercent = variation,
                PendingOcr = current.Count(r => !r.HasOcr || r.OcrStatus is "PENDING" or "QUEUED"),
                Unclassified = current.Count(r => !r.IsClassified),
                Interpretation = BuildSectorInterpretation(sector, current.Count, previous, variation, current.Count(r => !r.HasOcr || r.OcrStatus is "PENDING" or "QUEUED"), current.Count(r => !r.IsClassified))
            };
        }).OrderByDescending(s => s.PendingOcr + s.Unclassified).ThenByDescending(s => s.VariationPercent).Take(top <= 0 ? 20 : Math.Min(top, 100)).ToList();
    }

    private static IReadOnlyList<OperationalTrendDto> BuildOperationalTrends(CounterSnapshot current, CounterSnapshot previous)
    {
        return
        [
            BuildOperational("OCR pendente", current.PendingOcr, previous.PendingOcr, "Reprocessar fila OCR e verificar qualidade dos PDFs/imagens."),
            BuildOperational("OCR ERROR", current.OcrErrors, previous.OcrErrors, "Priorizar reprocessamento dos erros e tratar causas de captura ou conversão."),
            BuildOperational("Documentos sem classificação", current.Unclassified, previous.Unclassified, "Acionar responsáveis pela classificação arquivística e metadados obrigatórios."),
            BuildOperational("Documentos com OCR pesquisável", current.DocumentsWithOcr, previous.DocumentsWithOcr, "Manter cobertura OCR para apoiar auditoria e BI documental.")
        ];
    }

    private static OperationalTrendDto BuildOperational(string indicator, int current, int previous, string recommendation)
    {
        var variation = Variation(current, previous);
        return new OperationalTrendDto
        {
            Indicator = indicator,
            CurrentValue = current,
            PreviousValue = previous,
            VariationPercent = variation,
            Status = current > previous && indicator is not "Documentos com OCR pesquisável" ? "Piorando" : current < previous && indicator is not "Documentos com OCR pesquisável" ? "Melhorando" : "Estável",
            Recommendation = recommendation
        };
    }

    private static IReadOnlyList<HospitalAlertDto> BuildAlerts(IReadOnlyList<TrendKpiDto> terms, IReadOnlyList<SectorTrendDto> sectors, IReadOnlyList<OperationalTrendDto> operational, CounterSnapshot counters)
    {
        var alerts = new List<HospitalAlertDto>();
        foreach (var term in terms.Where(t => t.CurrentCount >= 5 && (t.VariationPercent > 50 || t.TrendDirection == "Novo")).Take(10))
        {
            var severity = term.TrendDirection == "Novo" ? "Médio/Alto" : "Alto";
            if ((term.Term.Equals("glosa", StringComparison.OrdinalIgnoreCase) || term.Term.Equals("faturamento", StringComparison.OrdinalIgnoreCase)) && term.VariationPercent > 0)
                severity = "Alto";
            if (CriticalTerms.Any(x => x.Equals(term.Term, StringComparison.OrdinalIgnoreCase)) && term.VariationPercent > 30)
                severity = "Alto";

            alerts.Add(new HospitalAlertDto
            {
                Id = StableGuid($"TERM:{term.Category}:{term.Term}"),
                Title = term.TrendDirection == "Novo" ? $"Termo novo relevante: {term.Term}" : $"Crescimento relevante de menções a {term.Term}",
                Category = term.Category,
                Severity = severity,
                Description = term.Interpretation,
                Recommendation = term.Category.Contains("FINANCEIRO", StringComparison.OrdinalIgnoreCase)
                    ? "Submeter amostra à auditoria administrativa/financeira e verificar impacto em glosas, faturamento e contratos."
                    : "Encaminhar leitura executiva para a área responsável e validar a tendência com os fluxos assistenciais/documentais.",
                RelatedDocumentCount = term.CurrentCount,
                ActionUrl = term.Examples.FirstOrDefault() is { } ex ? $"/Ged/Details?id={ex.DocumentId}" : null,
                Examples = term.Examples
            });
        }

        foreach (var op in operational)
        {
            if (op.Indicator == "OCR pendente" && op.CurrentValue > op.PreviousValue)
                alerts.Add(Alert("OP:OCR_PENDING", "Aumento de documentos sem OCR", "OPERACIONAL", "Médio", $"A fila ou pendência OCR cresceu {op.VariationPercent:N1}% no período.", op.Recommendation, op.CurrentValue));
            if (op.Indicator == "OCR ERROR" && op.CurrentValue > op.PreviousValue)
                alerts.Add(Alert("OP:OCR_ERROR", "Aumento de OCR ERROR", "OPERACIONAL", "Alto", $"Erros de OCR cresceram {op.VariationPercent:N1}% no período.", op.Recommendation, op.CurrentValue));
        }

        if (counters.TotalDocuments > 0 && Percent(counters.Unclassified, counters.TotalDocuments) > 30)
            alerts.Add(Alert("OP:UNCLASSIFIED", "Classificação arquivística atrasada", "OPERACIONAL", "Médio/Alto", $"{Percent(counters.Unclassified, counters.TotalDocuments):N1}% dos documentos do período estão sem classificação.", "Priorizar classificação para reduzir risco de retenção, busca e governança documental.", counters.Unclassified));

        var totalPending = sectors.Sum(s => s.PendingOcr + s.Unclassified);
        foreach (var sector in sectors.Where(s => totalPending > 0 && (s.PendingOcr + s.Unclassified) * 100m / totalPending > 40).Take(3))
        {
            alerts.Add(Alert($"SECTOR:{sector.Sector}", $"Pendências concentradas em {sector.Sector}", "SETOR", "Médio", $"O setor/pasta concentra {((sector.PendingOcr + sector.Unclassified) * 100m / totalPending):N1}% das pendências analisadas.", "Revisar fila, classificação e rotina de saneamento documental do setor/pasta.", sector.PendingOcr + sector.Unclassified));
        }

        return alerts.OrderByDescending(a => a.Severity is "Alto" or "Crítico").ThenByDescending(a => a.RelatedDocumentCount).ToList();
    }

    private static HospitalAlertDto Alert(string key, string title, string category, string severity, string description, string recommendation, int count) => new()
    {
        Id = StableGuid(key),
        Title = title,
        Category = category,
        Severity = severity,
        Description = description,
        Recommendation = recommendation,
        RelatedDocumentCount = count
    };

    private static IEnumerable<string> BuildWarnings(SchemaSnapshot schema, int currentOcr, int previousOcr, int top)
    {
        if (!schema.HasDocumentSearch)
            yield return "Índice textual de OCR indisponível. Tendências por termos dependem de OCR indexado.";
        if (currentOcr >= top || previousOcr >= top)
            yield return $"Análise textual limitada aos {top:N0} documentos OCR mais recentes por período para preservar desempenho.";
        yield return "Indicadores baseados em OCR documental. Utilizar para apoio gerencial e estatístico, não como diagnóstico clínico.";
    }

    private static DynamicParameters BuildSqlParams(HospitalTrendsFilter f, DateTime from, DateTime to)
    {
        var p = new DynamicParameters();
        p.Add("TenantId", f.TenantId);
        p.Add("From", from.Date);
        p.Add("ToExclusive", to.Date.AddDays(1));
        p.Add("FolderId", f.FolderId);
        p.Add("Sector", NullIfWhite(f.Sector));
        p.Add("DocumentType", NullIfWhite(f.DocumentType));
        p.Add("Top", f.Top);
        return p;
    }

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
        var top = filter.Top <= 0 ? DefaultTopDocuments : Math.Min(filter.Top, MaxTopDocuments);
        return new HospitalTrendsFilter { TenantId = filter.TenantId, From = from, To = to, CompareFrom = compareFrom, CompareTo = compareTo, FolderId = filter.FolderId, Sector = filter.Sector, DocumentType = filter.DocumentType, Category = filter.Category, Top = top, RefreshCache = filter.RefreshCache };
    }

    private static string BuildCacheKey(HospitalTrendsFilter f, string part) => $"HospitalTrends:{part}:{f.TenantId}:{f.From:yyyyMMdd}:{f.To:yyyyMMdd}:{f.CompareFrom:yyyyMMdd}:{f.CompareTo:yyyyMMdd}:{f.FolderId}:{f.Sector}:{f.DocumentType}:{f.Category}:{f.Top}";
    private static string BuildPeriodLabel(DateTime from, DateTime to) => $"{from:dd/MM/yyyy} a {to:dd/MM/yyyy}";
    private static string? NullIfWhite(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal Percent(int part, int total) => total <= 0 ? 0 : Math.Round(part * 100m / total, 2);
    private static decimal Variation(int current, int previous) => previous == 0 ? (current > 0 ? 100 : 0) : Math.Round((current - previous) * 100m / previous, 2);
    private static string Direction(int current, int previous, decimal variation) => previous == 0 && current > 0 ? "Novo" : variation > 5 ? "Crescimento" : variation < -5 ? "Queda" : "Estável";

    private static string RiskLevel(string term, string category, int current, decimal variation, string direction)
    {
        if (CriticalTerms.Any(x => x.Equals(term, StringComparison.OrdinalIgnoreCase)) && variation > 30) return "Alto";
        if ((term.Equals("glosa", StringComparison.OrdinalIgnoreCase) || term.Equals("faturamento", StringComparison.OrdinalIgnoreCase)) && variation > 0) return "Alto";
        if (current >= 5 && variation > 50) return "Alto";
        if (direction == "Novo" && current >= 5) return "Médio/Alto";
        if (category.Contains("JURÍDICO", StringComparison.OrdinalIgnoreCase) && current >= 3) return "Médio";
        return current >= 3 ? "Médio" : "Baixo";
    }

    private static string BuildTermInterpretation(string term, int current, int previous, decimal variation, string direction)
        => direction == "Novo"
            ? $"Menções a '{term}' surgiram no período atual em {current:N0} documentos analisados."
            : $"Menções a '{term}' {TrendVerb(direction)} {Math.Abs(variation):N1}% nos documentos analisados em relação ao período anterior ({current:N0} contra {previous:N0}).";

    private static string TrendVerb(string direction) => direction switch { "Crescimento" => "cresceram", "Queda" => "caíram", _ => "permaneceram estáveis em" };

    private static string BuildSectorInterpretation(string sector, int current, int previous, decimal variation, int pendingOcr, int unclassified)
        => $"O setor/pasta {sector} registrou {current:N0} documentos no período ({TrendVerb(Direction(current, previous, variation))} {Math.Abs(variation):N1}%), com {pendingOcr:N0} pendências OCR e {unclassified:N0} sem classificação.";

    private static List<RankingKpiDto> BuildRanking(IEnumerable<string?> values, int total)
        => values.Select(v => string.IsNullOrWhiteSpace(v) ? "Não informado" : v.Trim()).GroupBy(v => v).OrderByDescending(g => g.Count()).Take(10).Select(g => new RankingKpiDto { Label = g.Key, Value = g.Count(), Percentage = Percent(g.Count(), total) }).ToList();

    private static int CountOccurrences(string text, string normalizedTerm) => string.IsNullOrWhiteSpace(normalizedTerm) ? 0 : Regex.Matches(text, $@"(?<!\p{{L}}){Regex.Escape(normalizedTerm)}(?!\p{{L}})", RegexOptions.IgnoreCase).Count;
    private static string Normalize(string text) => string.Concat((text ?? string.Empty).Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Normalize(NormalizationForm.FormC).ToLowerInvariant();
    private static string MaskSensitive(string text) => LongNumberRegex.Replace(EmailRegex.Replace(CpfRegex.Replace(text ?? string.Empty, "***.***.***-**"), "***@***"), "******");

    private static DocumentSnippetDto ToSnippet(OcrRow row, string term) => new()
    {
        DocumentId = row.DocumentId,
        VersionId = row.VersionId == Guid.Empty ? null : row.VersionId,
        Title = MaskSensitive(row.Title),
        Folder = row.Folder,
        CreatedAt = row.CreatedAt,
        Snippet = ExtractSnippet(row.Text, term)
    };

    private static string ExtractSnippet(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Trecho OCR indisponível no recorte.";
        var idx = CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        if (idx < 0) idx = 0;
        var start = Math.Max(0, idx - 80);
        var len = Math.Min(SnippetLength, text.Length - start);
        var snippet = text.Substring(start, len).ReplaceLineEndings(" ");
        return MaskSensitive((start > 0 ? "..." : string.Empty) + snippet + (start + len < text.Length ? "..." : string.Empty));
    }

    private static Guid StableGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    private static HospitalTrendsDashboardDto EmptyDashboard(string warning) => new()
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        PeriodLabel = string.Empty,
        ComparePeriodLabel = string.Empty,
        Warnings = [warning]
    };

    private sealed class SchemaSnapshot
    {
        public bool HasDocumentSearch { get; set; }
        public bool HasOcrJob { get; set; }
    }

    private sealed class CounterSnapshot
    {
        public int TotalDocuments { get; set; }
        public int DocumentsWithOcr { get; set; }
        public int PendingOcr { get; set; }
        public int OcrErrors { get; set; }
        public int Unclassified { get; set; }
    }

    private sealed class DocumentRow
    {
        public Guid DocumentId { get; set; }
        public string Folder { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public bool IsClassified { get; set; }
        public bool HasOcr { get; set; }
        public string OcrStatus { get; set; } = string.Empty;
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
        public string Text { get; set; } = string.Empty;
    }
}
