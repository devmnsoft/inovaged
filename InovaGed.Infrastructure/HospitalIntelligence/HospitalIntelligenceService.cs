using System.Data;
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
    private const int OcrSnippetLength = 4000;
    private static readonly Regex MoneyRegex = new(@"(R\$\s?\d{1,3}(\.\d{3})*,\d{2}|\b\d{1,3}(\.\d{3})*,\d{2}\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var cacheKey = BuildCacheKey(f);
        if (!f.BypassCache && _cache.TryGetValue(cacheKey, out HospitalIntelligenceDashboardDto? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var total = await LoadTotalDocumentsAsync(conn, f, ct);
            var withOcr = await LoadDocumentsWithOcrAsync(conn, f, ct);
            var textRows = await LoadTextRowsAsync(conn, f, ct);

            var clinicalTerms = BuildClinicalTerms(textRows, f.Top);
            var financialSignals = BuildFinancialSignals(textRows);
            var operationalSignals = BuildOperationalSignals(textRows, total, withOcr);
            var alerts = BuildAlerts(clinicalTerms);

            var warning = string.Empty;
            if (textRows.Count == 0)
            {
                warning = "Texto OCR ainda não indexado para inteligência.";
            }

            var dto = BuildDashboard(total, withOcr, clinicalTerms, financialSignals, operationalSignals, alerts, warning);
            _cache.Set(cacheKey, dto, TimeSpan.FromMinutes(5));
            return dto;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL no HospitalIntelligence. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Não foi possível carregar alguns indicadores.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado no HospitalIntelligence. Tenant={TenantId}", f.TenantId);
            return EmptyDashboard("Indicadores indisponíveis no momento.");
        }
    }

    public async Task<IReadOnlyList<TermOccurrenceDto>> GetClinicalTermsAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await LoadTextRowsAsync(conn, f, ct);
            return BuildClinicalTerms(rows, f.Top);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar termos clínicos. Tenant={TenantId}", f.TenantId);
            return [];
        }
    }

    public async Task<IReadOnlyList<FinancialSignalDto>> GetFinancialSignalsAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await LoadTextRowsAsync(conn, f, ct);
            return BuildFinancialSignals(rows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar sinais financeiros. Tenant={TenantId}", f.TenantId);
            return [];
        }
    }

    public async Task<IReadOnlyList<OperationalSignalDto>> GetOperationalSignalsAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var total = await LoadTotalDocumentsAsync(conn, f, ct);
            var withOcr = await LoadDocumentsWithOcrAsync(conn, f, ct);
            var rows = await LoadTextRowsAsync(conn, f, ct);
            return BuildOperationalSignals(rows, total, withOcr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar sinais operacionais. Tenant={TenantId}", f.TenantId);
            return [];
        }
    }

    public async Task<IReadOnlyList<DocumentAlertDto>> GetCriticalAlertsAsync(HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await LoadTextRowsAsync(conn, f, ct);
            var clinical = BuildClinicalTerms(rows, f.Top);
            return BuildAlerts(clinical);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar alertas críticos. Tenant={TenantId}", f.TenantId);
            return [];
        }
    }

    private async Task<List<TextRow>> LoadTextRowsAsync(IDbConnection conn, HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "ged", "document_search", ct) || !await ColumnExistsAsync(conn, "ged", "document_search", "ocr_text", ct))
        {
            _logger.LogWarning("Tabela/coluna de OCR inexistente para Tenant={TenantId}", filter.TenantId);
            return [];
        }

        var sql = @"
select
    d.id as DocumentId,
    coalesce(d.title, 'Sem título') as Title,
    coalesce(f.name, 'Sem pasta') as Folder,
    d.created_at as CreatedAt,
    substring(s.ocr_text from 1 for @TextLength) as Text
from ged.document d
join ged.document_search s on s.document_id = d.id and s.tenant_id = d.tenant_id
left join ged.folder f on f.id = d.folder_id
where d.tenant_id = @TenantId
  and coalesce(s.ocr_text, '') <> ''
  and (@FolderId is null or d.folder_id = @FolderId)
  and (@Search is null or s.ocr_text ilike '%' || @Search || '%')
  and d.created_at >= @From
  and d.created_at < @ToExclusive
order by d.created_at desc
limit @Top;";

        var rows = await conn.QueryAsync<TextRow>(new CommandDefinition(sql, new
        {
            filter.TenantId,
            filter.FolderId,
            Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(),
            filter.From,
            ToExclusive = filter.To!.Value.Date.AddDays(1),
            Top = filter.Top,
            TextLength = OcrSnippetLength
        }, cancellationToken: ct));

        return rows.Select(r => r with { Text = MaskSensitive(r.Text ?? string.Empty) }).ToList();
    }

    private static IReadOnlyList<TermOccurrenceDto> BuildClinicalTerms(IReadOnlyList<TextRow> docs, int top)
    {
        var map = new Dictionary<string, string[]>
        {
            ["NEOPLASIA"] = ["neoplasia", "câncer", "tumor", "oncologia", "quimioterapia", "radioterapia", "biópsia", "metástase"],
            ["EMERGÊNCIA"] = ["sepse", "uti", "intubação", "hemorragia", "óbito", "avc", "iam"]
        };

        return BuildTermList(docs, map, top);
    }

    private static IReadOnlyList<FinancialSignalDto> BuildFinancialSignals(IReadOnlyList<TextRow> docs)
    {
        var map = new Dictionary<string, string[]>
        {
            ["Financeiro"] = ["nota fiscal", "contrato", "glosa", "faturamento", "pagamento", "convênio", "sus", "cobrança", "opme"]
        };

        var terms = BuildTermList(docs, map, 20);
        var list = terms.Select(t => new FinancialSignalDto
        {
            Indicator = t.Term,
            Count = t.Count,
            Description = "Sinal financeiro detectado via OCR",
            RiskLevel = t.RiskLevel,
            Documents = t.Examples
        }).ToList();

        var money = docs.Sum(d => MoneyRegex.Matches(d.Text).Count);
        list.Add(new FinancialSignalDto
        {
            Indicator = "Valores monetários detectados",
            Count = money,
            Description = "Detecção automática por regex; requer validação.",
            RiskLevel = money > 0 ? "Médio" : "Baixo"
        });

        return list;
    }

    private static IReadOnlyList<OperationalSignalDto> BuildOperationalSignals(IReadOnlyList<TextRow> docs, int totalDocuments, int withOcr)
    {
        var pending = Math.Max(0, totalDocuments - withOcr);
        return
        [
            new()
            {
                Indicator = "OCR pendente",
                Count = pending,
                Description = "Documentos sem OCR concluído.",
                Recommendation = "Priorizar processamento OCR.",
                RiskLevel = pending > 100 ? "Alto" : "Médio"
            },
            new()
            {
                Indicator = "Documentos analisados no recorte",
                Count = docs.Count,
                Description = "Volume efetivo de documentos usados nos indicadores.",
                Recommendation = "Ajustar filtros para análise detalhada.",
                RiskLevel = docs.Count == 0 ? "Médio" : "Baixo"
            }
        ];
    }

    private static IReadOnlyList<DocumentAlertDto> BuildAlerts(IReadOnlyList<TermOccurrenceDto> terms)
    {
        var high = terms.Where(x => x.RiskLevel is "Alto" or "Crítico").Take(10).ToList();
        return high.SelectMany(h => h.Examples.Take(1).Select(e => new DocumentAlertDto
        {
            DocumentId = e.DocumentId,
            VersionId = e.VersionId,
            Title = e.Title,
            Folder = e.Folder,
            AlertType = "Recorrência de termo crítico",
            Description = $"Termo '{h.Term}' recorrente.",
            RiskLevel = h.RiskLevel,
            CreatedAt = e.CreatedAt
        })).ToList();
    }

    private async Task<int> LoadTotalDocumentsAsync(IDbConnection conn, HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        const string sql = @"select count(*)
from ged.document d
where d.tenant_id = @TenantId
  and (@FolderId is null or d.folder_id = @FolderId)
  and d.created_at >= @From
  and d.created_at < @ToExclusive;";

        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            filter.TenantId,
            filter.FolderId,
            filter.From,
            ToExclusive = filter.To!.Value.Date.AddDays(1)
        }, cancellationToken: ct));
    }

    private async Task<int> LoadDocumentsWithOcrAsync(IDbConnection conn, HospitalIntelligenceFilter filter, CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "ged", "document_search", ct) || !await ColumnExistsAsync(conn, "ged", "document_search", "ocr_text", ct))
        {
            return 0;
        }

        const string sql = @"select count(distinct d.id)
from ged.document d
join ged.document_search s on s.document_id = d.id and s.tenant_id = d.tenant_id
where d.tenant_id = @TenantId
  and (@FolderId is null or d.folder_id = @FolderId)
  and d.created_at >= @From
  and d.created_at < @ToExclusive
  and coalesce(s.ocr_text, '') <> '';";

        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            filter.TenantId,
            filter.FolderId,
            filter.From,
            ToExclusive = filter.To!.Value.Date.AddDays(1)
        }, cancellationToken: ct));
    }

    private static HospitalIntelligenceDashboardDto BuildDashboard(int total, int withOcr, IReadOnlyList<TermOccurrenceDto> terms,
        IReadOnlyList<FinancialSignalDto> financial, IReadOnlyList<OperationalSignalDto> operational,
        IReadOnlyList<DocumentAlertDto> alerts, string? warning)
    {
        return new HospitalIntelligenceDashboardDto
        {
            TotalDocuments = total,
            DocumentsWithOcr = withOcr,
            DocumentsWithoutOcr = Math.Max(0, total - withOcr),
            OcrPending = Math.Max(0, total - withOcr),
            UnclassifiedDocuments = 0,
            CriticalDocuments = alerts.Count,
            Warning = warning,
            ClinicalTerms = terms,
            FinancialSignals = financial,
            OperationalSignals = operational,
            Alerts = alerts,
            Cards =
            [
                new() { Title = "Documentos analisados", Value = total.ToString(), Color = "primary", Icon = "bi-files" },
                new() { Title = "Documentos com OCR", Value = withOcr.ToString(), Color = "success", Icon = "bi-file-earmark-check" },
                new() { Title = "OCR pendente", Value = Math.Max(0, total - withOcr).ToString(), Color = "warning", Icon = "bi-hourglass-split" },
                new() { Title = "Alertas críticos", Value = alerts.Count(a => a.RiskLevel == "Crítico").ToString(), Color = "danger", Icon = "bi-exclamation-triangle" },
                new() { Title = "Possíveis documentos financeiros", Value = financial.Sum(x => x.Count).ToString(), Color = "info", Icon = "bi-cash" },
                new() { Title = "Termos clínicos encontrados", Value = terms.Sum(x => x.Count).ToString(), Color = "secondary", Icon = "bi-heart-pulse" }
            ]
        };
    }

    private static HospitalIntelligenceDashboardDto EmptyDashboard(string warning) => new()
    {
        Warning = warning,
        Cards =
        [
            new() { Title = "Documentos analisados", Value = "0", Color = "primary", Icon = "bi-files" },
            new() { Title = "Documentos com OCR", Value = "0", Color = "success", Icon = "bi-file-earmark-check" },
            new() { Title = "OCR pendente", Value = "0", Color = "warning", Icon = "bi-hourglass-split" }
        ]
    };

    private static HospitalIntelligenceFilter NormalizeFilter(HospitalIntelligenceFilter filter)
    {
        var now = DateTime.UtcNow.Date;
        var from = filter.From?.Date ?? now.AddDays(-90);
        var to = filter.To?.Date ?? now;
        if (from > to)
        {
            (from, to) = (to, from);
        }

        return new HospitalIntelligenceFilter
        {
            TenantId = filter.TenantId,
            From = from,
            To = to,
            FolderId = filter.FolderId,
            Sector = filter.Sector,
            DocumentType = filter.DocumentType,
            Search = filter.Search,
            IncludeClinicalTerms = filter.IncludeClinicalTerms,
            IncludeFinancial = filter.IncludeFinancial,
            IncludeOperational = filter.IncludeOperational,
            BypassCache = filter.BypassCache,
            Top = filter.Top <= 0 ? DefaultTopDocuments : Math.Min(filter.Top, MaxTopDocuments)
        };
    }

    private static string BuildCacheKey(HospitalIntelligenceFilter f)
        => $"HospitalIntelligence:{f.TenantId}:{f.From:yyyyMMdd}:{f.To:yyyyMMdd}:{f.FolderId}:{f.Sector}:{f.DocumentType}:{f.Search}:{f.Top}";

    private static List<TermOccurrenceDto> BuildTermList(IReadOnlyList<TextRow> docs, IReadOnlyDictionary<string, string[]> map, int top)
    {
        var result = new List<TermOccurrenceDto>();
        foreach (var kv in map)
        {
            foreach (var term in kv.Value)
            {
                var matched = docs.Where(d => d.Text.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
                if (!matched.Any())
                {
                    continue;
                }

                var count = docs.Count(d => d.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
                result.Add(new TermOccurrenceDto
                {
                    Category = kv.Key,
                    Term = term,
                    Count = count,
                    DocumentCount = matched.Count,
                    Percentage = docs.Count == 0 ? 0 : Math.Round((decimal)count * 100 / docs.Count, 2),
                    RiskLevel = count > 20 ? "Crítico" : count > 10 ? "Alto" : count > 3 ? "Médio" : "Baixo",
                    Examples = matched.Select(m => new DocumentReferenceDto
                    {
                        DocumentId = m.DocumentId,
                        Title = m.Title,
                        Folder = m.Folder,
                        CreatedAt = m.CreatedAt,
                        Snippet = ExtractSnippet(m.Text, term)
                    }).ToList()
                });
            }
        }

        return result.OrderByDescending(x => x.Count).Take(top).ToList();
    }

    private static string ExtractSnippet(string text, string term)
    {
        var idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return "Trecho indisponível.";
        }

        var start = Math.Max(0, idx - 40);
        var len = Math.Min(120, text.Length - start);
        return text.Substring(start, len);
    }

    private static string MaskSensitive(string text) => Regex.Replace(text, @"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", "***.***.***-**");

    private static async Task<bool> TableExistsAsync(IDbConnection conn, string schema, string table, CancellationToken ct)
        => await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.tables where table_schema=@schema and table_name=@table)", new { schema, table }, cancellationToken: ct));

    private static async Task<bool> ColumnExistsAsync(IDbConnection conn, string schema, string table, string column, CancellationToken ct)
        => await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.columns where table_schema=@schema and table_name=@table and column_name=@column)", new { schema, table, column }, cancellationToken: ct));

    private sealed record TextRow(Guid DocumentId, string Title, string Folder, DateTime? CreatedAt, string Text);
}
