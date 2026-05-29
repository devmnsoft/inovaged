using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.HospitalAnalytics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.HospitalAnalytics;

public sealed class HospitalOcrAnalyticsService : IHospitalOcrAnalyticsService
{
    private const int DefaultTopDocuments = 1000;
    private const int MaxTopDocuments = 5000;
    private const int TextPreviewLength = 4000;
    private const int SnippetLength = 200;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly Regex MoneyRegex = new(@"(?:R\$\s*)?\b(?:\d{1,3}(?:\.\d{3})+|\d{1,9}),\d{2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LongNumberRegex = new(@"\b\d{6,}\b", RegexOptions.Compiled);

    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HospitalOcrAnalyticsService> _logger;

    public HospitalOcrAnalyticsService(IDbConnectionFactory db, IMemoryCache cache, ILogger<HospitalOcrAnalyticsService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<HospitalOcrAnalyticsSnapshotDto> BuildSnapshotAsync(HospitalOcrAnalyticsFilter filter, CancellationToken ct)
    {
        var f = NormalizeFilter(filter);
        var cacheKey = BuildCacheKey(f);
        if (!f.RefreshCache && _cache.TryGetValue(cacheKey, out HospitalOcrAnalyticsSnapshotDto? cached) && cached is not null)
            return cached;

        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        var snapshot = await LoadCountersAsync(conn, f, schema, ct);
        snapshot.Rows = await LoadTextRowsAsync(conn, f, schema, ct);
        snapshot.Warnings.AddRange(BuildWarnings(schema, snapshot.Rows.Count, f.Top));

        _cache.Set(cacheKey, snapshot, CacheTtl);
        return snapshot;
    }

    public Task<IReadOnlyList<TermMatchDto>> AnalyzeTermsAsync(HospitalOcrAnalyticsSnapshotDto snapshot, IReadOnlyList<TermDictionaryItemDto> dictionary, CancellationToken ct)
    {
        var matches = new List<TermMatchDto>();
        foreach (var item in dictionary)
        {
            ct.ThrowIfCancellationRequested();
            var needles = new[] { item.Term }.Concat(item.Synonyms ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var occurrences = 0;
            var docIds = new HashSet<Guid>();
            var examples = new List<DocumentSnippetDto>();

            foreach (var row in snapshot.Rows)
            {
                var rowOccurrences = needles.Sum(term => CountOccurrences(Normalize(row.TextPreview), Normalize(term)));
                if (rowOccurrences <= 0)
                    continue;

                occurrences += rowOccurrences;
                docIds.Add(row.DocumentId);
                if (examples.Count < 3)
                    examples.Add(ToSnippet(row, needles.First(term => CountOccurrences(Normalize(row.TextPreview), Normalize(term)) > 0)));
            }

            if (occurrences > 0)
            {
                matches.Add(new TermMatchDto
                {
                    Term = item.Term,
                    Category = item.Category,
                    RiskLevel = ResolveRiskLevel(item, docIds.Count, occurrences),
                    Occurrences = occurrences,
                    DocumentCount = docIds.Count,
                    Examples = examples
                });
            }
        }

        return Task.FromResult<IReadOnlyList<TermMatchDto>>(matches.OrderByDescending(x => x.DocumentCount).ThenByDescending(x => x.Occurrences).ToList());
    }

    public Task<IReadOnlyList<MoneySignalDto>> AnalyzeMoneySignalsAsync(HospitalOcrAnalyticsSnapshotDto snapshot, CancellationToken ct)
    {
        var signals = new List<MoneySignalDto>();
        foreach (var row in snapshot.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var matches = MoneyRegex.Matches(row.TextPreview ?? string.Empty).Cast<Match>().Take(5);
            foreach (var match in matches)
            {
                signals.Add(new MoneySignalDto
                {
                    DocumentId = row.DocumentId,
                    VersionId = row.VersionId,
                    Title = row.Title,
                    Folder = SafeFolder(row.Folder),
                    MoneyText = match.Value,
                    ParsedValue = TryParseMoney(match.Value),
                    Snippet = ExtractSnippet(row.TextPreview, match.Value)
                });
            }
        }

        return Task.FromResult<IReadOnlyList<MoneySignalDto>>(signals);
    }

    private async Task<SchemaSnapshot> LoadSchemaAsync(IDbConnection conn, CancellationToken ct)
    {
        const string sql = @"
select
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='document_search') as ""HasDocumentSearch"",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_search' and column_name='ocr_text') as ""HasDocumentSearchOcrText"",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_search' and column_name='version_id') as ""HasDocumentSearchVersionId"",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_search' and column_name='title') as ""HasDocumentSearchTitle"",
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='ocr_job') as ""HasOcrJob"",
 exists(select 1 from information_schema.tables where table_schema='ged' and table_name='preview_status') as ""HasPreviewStatus"";";
        return await conn.QuerySingleAsync<SchemaSnapshot>(new CommandDefinition(sql, cancellationToken: ct));
    }

    private async Task<HospitalOcrAnalyticsSnapshotDto> LoadCountersAsync(IDbConnection conn, HospitalOcrAnalyticsFilter f, SchemaSnapshot schema, CancellationToken ct)
    {
        var ocrJobJoin = schema.HasOcrJob ? "left join ged.ocr_job oj on oj.tenant_id=d.tenant_id and oj.document_version_id=d.current_version_id" : string.Empty;
        var ocrStatusExpr = schema.HasOcrJob ? "upper(coalesce(oj.status::text, ''))" : "''";
        var hasOcrExpr = schema.CanReadOcr ? "exists(select 1 from ged.document_search ds where ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> '')" : "false";
        var sql = $@"
with filtered as (
 select d.id,
        d.folder_id,
        coalesce(d.classification_id, d.classification_version_id) classification_id,
        {hasOcrExpr} has_ocr,
        {ocrStatusExpr} ocr_status
 from ged.document d
 left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
 left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
 {ocrJobJoin}
 where d.tenant_id=@TenantId::uuid and coalesce(d.reg_status,'A')='A'
   and (@From::timestamp is null or d.created_at >= @From::timestamp)
   and (@To::timestamp is null or d.created_at < @To::timestamp)
   and (@FolderId::uuid is null or d.folder_id=@FolderId::uuid)
   and (@Sector::text is null or fol.name ilike '%' || @Sector::text || '%')
   and (@DocumentType::text is null or dt.name ilike '%' || @DocumentType::text || '%')
   and (@Search::text is null or d.title ilike '%' || @Search::text || '%')
)
select now() as ""GeneratedAt"",
       count(distinct id)::int as ""TotalDocuments"",
       count(distinct id) filter (where has_ocr)::int as ""DocumentsWithOcr"",
       count(distinct id) filter (where not has_ocr)::int as ""DocumentsWithoutOcr"",
       count(distinct id) filter (where not has_ocr or ocr_status in ('PENDING','QUEUED'))::int as ""OcrPending"",
       count(distinct id) filter (where ocr_status in ('PROCESSING','RUNNING','IN_PROGRESS'))::int as ""OcrProcessing"",
       count(distinct id) filter (where has_ocr or ocr_status in ('COMPLETED','DONE','SUCCESS'))::int as ""OcrCompleted"",
       count(distinct id) filter (where ocr_status in ('ERROR','FAILED'))::int as ""OcrErrors"",
       count(distinct id) filter (where ocr_status in ('CANCELLED','CANCELED'))::int as ""OcrCancelled"",
       count(distinct id) filter (where classification_id is null)::int as ""UnclassifiedDocuments"",
       count(distinct id) filter (where classification_id is not null)::int as ""ClassifiedDocuments""
from filtered;";
        return await conn.QuerySingleAsync<HospitalOcrAnalyticsSnapshotDto>(new CommandDefinition(sql, BuildSqlParams(f), cancellationToken: ct));
    }

    private async Task<List<OcrDocumentTextRowDto>> LoadTextRowsAsync(IDbConnection conn, HospitalOcrAnalyticsFilter f, SchemaSnapshot schema, CancellationToken ct)
    {
        if (!schema.CanReadOcr)
            return [];

        var versionExpr = schema.HasDocumentSearchVersionId ? "ds.version_id" : "d.current_version_id";
        var dsTitleExpr = schema.HasDocumentSearchTitle ? "ds.title" : "null";
        var ocrJobJoin = schema.HasOcrJob ? "left join ged.ocr_job oj on oj.tenant_id=d.tenant_id and oj.document_version_id=d.current_version_id" : string.Empty;
        var ocrStatusExpr = schema.HasOcrJob ? "upper(coalesce(oj.status::text, ''))" : "case when coalesce(ds.ocr_text,'') <> '' then 'COMPLETED' else '' end";
        var sql = $@"
select distinct on (d.id) d.id as ""DocumentId"",
       {versionExpr} as ""VersionId"",
       coalesce(d.title, {dsTitleExpr}, 'Sem título') as ""Title"",
       coalesce(fol.name, 'Sem pasta') as ""Folder"",
       coalesce(fol.name, '') as ""Sector"",
       coalesce(dt.name, 'Sem tipo') as ""DocumentType"",
       d.created_at as ""CreatedAt"",
       {ocrStatusExpr} as ""OcrStatus"",
       substring(ds.ocr_text from 1 for {TextPreviewLength}) as ""TextPreview"",
       coalesce(d.classification_id, d.classification_version_id) is not null as ""IsClassified""
from ged.document d
join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id and coalesce(ds.ocr_text,'') <> ''
left join ged.folder fol on fol.id=d.folder_id and fol.tenant_id=d.tenant_id
left join ged.document_type dt on dt.id=d.type_id and dt.tenant_id=d.tenant_id
{ocrJobJoin}
where d.tenant_id=@TenantId::uuid and coalesce(d.reg_status,'A')='A'
  and (@From::timestamp is null or d.created_at >= @From::timestamp)
  and (@To::timestamp is null or d.created_at < @To::timestamp)
  and (@FolderId::uuid is null or d.folder_id=@FolderId::uuid)
  and (@Sector::text is null or fol.name ilike '%' || @Sector::text || '%')
  and (@DocumentType::text is null or dt.name ilike '%' || @DocumentType::text || '%')
  and (@Search::text is null or ds.ocr_text ilike '%' || @Search::text || '%' or d.title ilike '%' || @Search::text || '%')
order by d.id, d.created_at desc
limit @Top::int;";
        var rows = (await conn.QueryAsync<OcrDocumentTextRowDto>(new CommandDefinition(sql, BuildSqlParams(f), cancellationToken: ct))).ToList();
        foreach (var row in rows)
        {
            row.Title = MaskSensitive(row.Title);
            row.TextPreview = LimitText(MaskSensitive(row.TextPreview));
        }

        return rows;
    }

    private static DynamicParameters BuildSqlParams(HospitalOcrAnalyticsFilter f)
    {
        var p = new DynamicParameters();
        p.Add("TenantId", f.TenantId, DbType.Guid);
        p.Add("From", f.From, DbType.DateTime);
        p.Add("To", f.To, DbType.DateTime);
        p.Add("FolderId", f.FolderId, DbType.Guid);
        p.Add("Sector", NullIfWhiteSpace(f.Sector), DbType.String);
        p.Add("DocumentType", NullIfWhiteSpace(f.DocumentType), DbType.String);
        p.Add("Search", NullIfWhiteSpace(f.Search), DbType.String);
        p.Add("Top", NormalizeTop(f.Top), DbType.Int32);
        return p;
    }

    private static IEnumerable<string> BuildWarnings(SchemaSnapshot schema, int rowCount, int top)
    {
        if (!schema.HasDocumentSearch)
            yield return "Tabela ged.document_search não encontrada. Indicadores textuais de OCR indisponíveis.";
        else if (!schema.HasDocumentSearchOcrText)
            yield return "Coluna ged.document_search.ocr_text não encontrada. Indicadores textuais de OCR indisponíveis.";
        if (!schema.HasOcrJob)
            yield return "Tabela ged.ocr_job não encontrada. Status detalhado de fila OCR pode ficar limitado.";
        if (rowCount >= top)
            yield return $"Análise limitada aos {top:N0} documentos OCR mais recentes do recorte para preservar desempenho.";
    }

    private static HospitalOcrAnalyticsFilter NormalizeFilter(HospitalOcrAnalyticsFilter filter)
    {
        var top = NormalizeTop(filter.Top);
        return new HospitalOcrAnalyticsFilter
        {
            TenantId = filter.TenantId,
            From = filter.From,
            To = filter.To,
            FolderId = filter.FolderId,
            Sector = NullIfWhiteSpace(filter.Sector),
            DocumentType = NullIfWhiteSpace(filter.DocumentType),
            Search = NullIfWhiteSpace(filter.Search),
            Top = top,
            RefreshCache = filter.RefreshCache
        };
    }

    private static string BuildCacheKey(HospitalOcrAnalyticsFilter f) => $"HospitalOcrAnalytics:{f.TenantId}:{f.From:O}:{f.To:O}:{f.FolderId}:{f.Sector}:{f.DocumentType}:{f.Search}:{f.Top}";
    private static int NormalizeTop(int top) => top <= 0 ? DefaultTopDocuments : Math.Min(top, MaxTopDocuments);
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string LimitText(string text) => text.Length > TextPreviewLength ? text[..TextPreviewLength] : text;
    private static string SafeFolder(string? value) => string.IsNullOrWhiteSpace(value) ? "Sem pasta" : value.Trim();
    private static int CountOccurrences(string text, string term) => string.IsNullOrWhiteSpace(term) ? 0 : Regex.Matches(text, $@"(?<!\p{{L}}){Regex.Escape(term)}(?!\p{{L}})", RegexOptions.IgnoreCase).Count;
    private static string Normalize(string text) => string.Concat((text ?? string.Empty).Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).Normalize(NormalizationForm.FormC).ToLowerInvariant();
    private static string MaskSensitive(string? text) => LongNumberRegex.Replace(EmailRegex.Replace(CpfRegex.Replace(text ?? string.Empty, "***.***.***-**"), "***@***"), "******");
    private static decimal? TryParseMoney(string value) => decimal.TryParse(value.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(".", ""), NumberStyles.Number, new CultureInfo("pt-BR"), out var parsed) ? parsed : null;

    private static string ResolveRiskLevel(TermDictionaryItemDto item, int docs, int occurrences)
    {
        if (item.Category.Contains("URGÊNCIA", StringComparison.OrdinalIgnoreCase) || item.RiskLevel.Equals("Alto", StringComparison.OrdinalIgnoreCase) || docs >= 20 || occurrences >= 50)
            return "Alto";
        return docs >= 5 ? "Médio" : string.IsNullOrWhiteSpace(item.RiskLevel) ? "Baixo" : item.RiskLevel;
    }

    private static DocumentSnippetDto ToSnippet(OcrDocumentTextRowDto row, string term) => new()
    {
        DocumentId = row.DocumentId,
        VersionId = row.VersionId,
        Title = row.Title,
        Folder = SafeFolder(row.Folder),
        CreatedAt = row.CreatedAt,
        Snippet = ExtractSnippet(row.TextPreview, term)
    };

    private static string ExtractSnippet(string? text, string term)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Trecho OCR indisponível no recorte.";

        var idx = CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        if (idx < 0)
            idx = 0;
        var start = Math.Max(0, idx - 80);
        var len = Math.Min(SnippetLength, text.Length - start);
        var snippet = text.Substring(start, len).ReplaceLineEndings(" ");
        return MaskSensitive((start > 0 ? "..." : string.Empty) + snippet + (start + len < text.Length ? "..." : string.Empty));
    }

    private sealed class SchemaSnapshot
    {
        public bool HasDocumentSearch { get; set; }
        public bool HasDocumentSearchOcrText { get; set; }
        public bool HasDocumentSearchVersionId { get; set; }
        public bool HasDocumentSearchTitle { get; set; }
        public bool HasOcrJob { get; set; }
        public bool HasPreviewStatus { get; set; }
        public bool CanReadOcr => HasDocumentSearch && HasDocumentSearchOcrText;
    }
}
