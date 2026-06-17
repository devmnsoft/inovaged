using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SmartSearch;
using InovaGed.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SmartSearch;

public sealed class SmartSearchRepository : ISmartSearchRepository, InovaGed.Application.Ged.Search.IGedSmartSearchRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentOcrMetadataExtractor _extractor;
    private readonly ILogger<SmartSearchRepository> _logger;

    public SmartSearchRepository(IDbConnectionFactory db, IDocumentOcrMetadataExtractor extractor, ILogger<SmartSearchRepository> logger)
    {
        _db = db;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<SmartSearchResult> SearchAsync(SmartSearchIntent intent, UserDocumentScope scope, SmartSearchRequest request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 50);
        var offset = (page - 1) * pageSize;
        await using var conn = await _db.OpenAsync(ct);
        var hasSmartIndex = await ExistsAsync(conn, "ged.document_search_index", ct);
        var hasLegacyOcr = await ExistsAsync(conn, "ged.document_search", ct);
        var fallbackSql = hasLegacyOcr ? FallbackSql : DirectSql;
        var sql = hasSmartIndex ? SmartIndexSql : fallbackSql;
        var query = string.IsNullOrWhiteSpace(intent.ExpandedQuery) ? intent.OriginalQuery : intent.ExpandedQuery;
        var p = new DynamicParameters();
        p.Add("tenantId", scope.TenantId, DbType.Guid);
        p.Add("query", query, DbType.String);
        p.Add("originalQuery", intent.OriginalQuery, DbType.String);
        p.Add("likeQuery", $"%{EscapeLike(query)}%", DbType.String);
        p.Add("likeOriginalQuery", $"%{EscapeLike(intent.OriginalQuery)}%", DbType.String);
        var numericTerm = Regex.Match(intent.OriginalQuery ?? string.Empty, @"\d{4,}").Value;
        p.Add("numericTerm", string.IsNullOrWhiteSpace(numericTerm) ? null : numericTerm, DbType.String);
        p.Add("likeNumericTerm", string.IsNullOrWhiteSpace(numericTerm) ? null : $"%{numericTerm}%", DbType.String);
        var tokens = SmartSearchTextNormalizer.Tokenize(intent.OriginalQuery);
        p.Add("tokens", tokens.ToArray());
        p.Add("tokenCount", tokens.Count, DbType.Int32);
        p.Add("patientName", intent.PatientName, DbType.String);
        p.Add("likePatientName", string.IsNullOrWhiteSpace(intent.PatientName) ? null : $"%{EscapeLike(intent.PatientName)}%", DbType.String);
        p.Add("documentType", intent.DocumentType ?? request.DocumentType, DbType.String);
        p.Add("examType", intent.ExamType, DbType.String);
        p.Add("clinicalTerms", intent.ClinicalTerms.ToArray());
        p.Add("medicalRecordNumber", intent.MedicalRecordNumber, DbType.String);
        p.Add("protocolNumber", intent.ProtocolNumber, DbType.String);
        p.Add("age", intent.Age, DbType.Int32);
        p.Add("ageFrom", intent.AgeFrom, DbType.Int32);
        p.Add("ageTo", intent.AgeTo, DbType.Int32);
        p.Add("year", intent.Year, DbType.Int32);
        p.Add("folderId", request.FolderId, DbType.Guid);
        var fromUtc = PostgresDateTimeHelper.ToUtc(intent.From);
        var toUtc = PostgresDateTimeHelper.ToUtc(intent.To);
        var dateFilters = new StringBuilder();
        if (fromUtc.HasValue)
        {
            dateFilters.AppendLine("and d.created_at >= @from");
            p.Add("from", fromUtc.Value, DbType.DateTime);
            intent.From = fromUtc.Value;
        }

        if (toUtc.HasValue)
        {
            dateFilters.AppendLine("and d.created_at < @to");
            p.Add("to", toUtc.Value, DbType.DateTime);
            intent.To = toUtc.Value;
        }

        sql = sql.Replace("/*DATE_FILTERS*/", dateFilters.ToString());
        p.Add("offset", offset, DbType.Int32);
        p.Add("limit", pageSize, DbType.Int32);

        var rows = (await conn.QueryAsync<SearchRow>(new CommandDefinition(sql, p, cancellationToken: ct, commandTimeout: 30))).ToList();
        var indexCount = hasSmartIndex ? rows.Count : 0;
        var fallbackCount = 0;
        if (hasSmartIndex)
        {
            var fallbackRows = (await conn.QueryAsync<SearchRow>(new CommandDefinition(fallbackSql.Replace("/*DATE_FILTERS*/", dateFilters.ToString()), p, cancellationToken: ct, commandTimeout: 30))).ToList();
            fallbackCount = fallbackRows.Count;
            rows = rows.Concat(fallbackRows)
                .GroupBy(x => x.DocumentId)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Title)
                .Take(pageSize)
                .ToList();
        }
        if (rows.Count == 0)
        {
            _logger.LogWarning("GED_SMART_SEARCH_NO_RESULT Tenant={TenantId} User={UserId} Query={Query} Tokens={Tokens} Scope={Scope} FolderId={FolderId} IndexCount={IndexCount} FallbackCount={FallbackCount} CorrelationId={CorrelationId}",
                request.TenantId, request.UserId, RedactSensitive(request.Query), string.Join(',', intent.Keywords.Take(12)), request.Source, request.FolderId, indexCount, fallbackCount, string.Empty);
        }
        var total = rows.FirstOrDefault()?.TotalRows ?? 0;
        var items = rows.Select(r => Map(r, intent)).ToList();
        return new SmartSearchResult
        {
            Intent = intent,
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize),
            Warning = rows.Count == 0 ? $"Nenhum resultado encontrado. Tokens={string.Join(",", tokens)}; searchedIndexed={hasSmartIndex}; searchedDirect=true; searchedOcr={hasLegacyOcr}; indexAvailable={hasSmartIndex}; indexCount={indexCount}; fallbackCount={fallbackCount}. Tentamos buscar em nome, arquivo, pasta e OCR." : (hasSmartIndex ? null : "Índice inteligente indisponível; usando fallback direto em documentos, arquivos, pastas e OCR legado."),
            Tokens = tokens,
            SearchedDirect = true,
            SearchedIndex = hasSmartIndex,
            SearchedOcr = hasLegacyOcr,
            IndexAvailable = hasSmartIndex,
            FallbackCount = fallbackCount,
            Message = rows.Count == 0 ? "Não encontrei documentos com esse contexto." : null,
            Suggestions = rows.Count == 0 ? intent.ClinicalTerms.Concat(new[] { "Tentar: neoplasia mamária", "Tentar: carcinoma mamário", "Buscar em todo GED", "Buscar apenas por OCR", "Ver diagnóstico SmartSearch", "Reindexar busca (ADMIN)" }).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray() : []
        };
    }

    public async Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, string? term, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return [];
        const string sql = """
select term as "Text", coalesce(category,'Sinônimo') as "Category"
from ged.search_synonym
where tenant_id = @tenantId and coalesce(reg_status,'A')='A' and (term ilike @like or synonym ilike @like)
union all
select synonym as "Text", coalesce(category,'Sinônimo') as "Category"
from ged.search_synonym
where tenant_id = @tenantId and coalesce(reg_status,'A')='A' and (term ilike @like or synonym ilike @like)
limit 12
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return (await conn.QueryAsync<SmartSearchSuggestion>(new CommandDefinition(sql, new { tenantId, like = $"%{EscapeLike(term)}%" }, cancellationToken: ct))).ToList();
        }
        catch { return []; }
    }

    public async Task<string?> GetDocumentOcrAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
select ocr_text from ged.document_search
where tenant_id=@tenantId and document_id=@documentId and nullif(ocr_text,'') is not null
order by updated_at desc nulls last limit 1
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    public async Task LogQueryAsync(SmartSearchRequest request, SmartSearchIntent intent, int resultsCount, long durationMs, CancellationToken ct)
    {
        const string sql = """
insert into ged.search_query_log(tenant_id, user_id, query_text, query_hash, interpreted_json, results_count, duration_ms, created_at)
values (@TenantId, @UserId, @QueryText, @QueryHash, cast(@InterpretedJson as jsonb), @ResultsCount, @DurationMs, @CreatedAt)
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var redacted = RedactSensitive(request.Query);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { request.TenantId, request.UserId, QueryText = redacted.Length > 300 ? redacted[..300] : redacted, QueryHash = Sha256(request.Query), InterpretedJson = JsonSerializer.Serialize(intent), ResultsCount = resultsCount, DurationMs = (int)Math.Min(durationMs, int.MaxValue), CreatedAt = DateTime.UtcNow }, cancellationToken: ct));
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Não foi possível registrar log da busca inteligente."); }
    }

    public async Task LogAccessAsync(Guid tenantId, Guid userId, Guid documentId, string source, string action, CancellationToken ct)
    {
        const string sql = "insert into ged.document_access_stat(tenant_id, document_id, user_id, source, action) values (@tenantId, @documentId, @userId, @source, @action)";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, userId, source, action }, cancellationToken: ct));
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Não foi possível registrar estatística de acesso."); }
    }

    public async Task<SmartSearchStatistics> GetStatisticsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = """
select
(select count(*)::int from ged.search_query_log where tenant_id=@tenantId and created_at::date = now()::date) as "SearchesToday",
(select count(*)::int from ged.search_query_log where tenant_id=@tenantId and results_count=0) as "SearchesWithoutResult",
(select coalesce(avg(duration_ms),0)::int from ged.search_query_log where tenant_id=@tenantId and created_at >= now() - interval '30 days') as "AverageDurationMs",
(select case when count(*)=0 then 0 else round(100.0 * count(*) filter(where nullif(ocr_text,'') is not null) / count(*),2) end from ged.document_search where tenant_id=@tenantId) as "OcrAvailablePercent"
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var stats = await conn.QuerySingleAsync<SmartSearchStatistics>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
            stats.OcrMissingPercent = Math.Max(0, 100 - stats.OcrAvailablePercent);
            stats.TopTerms = await TopAsync(conn, "select coalesce(jsonb_path_query_first(interpreted_json, '$.Keywords[0]')#>>'{}', query_text) k, count(*)::int c from ged.search_query_log where tenant_id=@tenantId group by 1 order by 2 desc limit 8", tenantId, ct);
            stats.TopDocumentTypes = await TopAsync(conn, "select coalesce(interpreted_json->>'DocumentType','Não informado') k, count(*)::int c from ged.search_query_log where tenant_id=@tenantId group by 1 order by 2 desc limit 8", tenantId, ct);
            stats.MostAccessedDocuments = await TopAsync(conn, "select document_id::text k, count(*)::int c from ged.document_access_stat where tenant_id=@tenantId group by 1 order by 2 desc limit 8", tenantId, ct);
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Estatísticas de busca inteligente indisponíveis; execute a migration 2026_06_smart_search.sql.");
            return new SmartSearchStatistics();
        }
    }

    public async Task<int> ReindexAsync(Guid tenantId, Guid? documentId, CancellationToken ct)
    {
        const string sql = """
insert into ged.document_search_index(document_id, version_id, document_version_id, tenant_id, title, file_name, document_type, classification, classification_name, folder_id, folder_name, patient_name, extracted_age, extracted_year, extracted_terms, ocr_text, search_text, search_vector, last_indexed_at, updated_at)
select d.id, coalesce(v.id, ds.version_id), coalesce(v.id, ds.version_id), d.tenant_id, coalesce(d.title, ds.file_name, v.file_name, 'Documento'), coalesce(v.file_name, ds.file_name), null, null, null, d.folder_id, f.name,
null, null, null, array[]::text[], ds.ocr_text,
concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text),
to_tsvector('portuguese', coalesce(concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text),'')), now(), now()
from ged.document d
left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id
left join ged.document_version v on v.tenant_id=d.tenant_id and (v.id=coalesce(ds.version_id, d.current_version_id) or v.id=d.current_version_id)
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and (@documentId is null or d.id=@documentId)
on conflict (tenant_id, document_id) do update set
version_id=excluded.version_id, document_version_id=excluded.document_version_id, title=excluded.title, file_name=excluded.file_name, folder_id=excluded.folder_id, folder_name=excluded.folder_name,
ocr_text=excluded.ocr_text, search_text=excluded.search_text, search_vector=excluded.search_vector, last_indexed_at=now(), updated_at=now()
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct, commandTimeout: 120));
    }

    private static SmartSearchResultItem Map(SearchRow r, SmartSearchIntent intent)
    {
        var reasons = new List<SmartSearchResultReason>();
        if (!string.IsNullOrWhiteSpace(intent.PatientName) && Contains(r.PatientName + " " + r.SearchText, intent.PatientName)) reasons.Add(new() { Reason = "Nome parecido", Evidence = intent.PatientName!, Weight = 40 });
        if (intent.Year.HasValue && r.Year == intent.Year) reasons.Add(new() { Reason = "Ano/período compatível", Evidence = intent.Year.Value.ToString(), Weight = 15 });
        if (intent.Age.HasValue && r.Age.HasValue && Math.Abs(r.Age.Value - intent.Age.Value) <= 1) reasons.Add(new() { Reason = "Idade aproximada", Evidence = r.Age.Value.ToString(), Weight = 10 });
        foreach (var term in intent.ClinicalTerms.Take(6).Where(t => Contains(r.SearchText, t))) reasons.Add(new() { Reason = r.HasOcr ? "OCR menciona termo relacionado" : "Metadados mencionam termo relacionado", Evidence = term, Weight = 80 });
        foreach (var term in intent.Keywords.Take(4).Where(t => Contains(r.FileName, t))) reasons.Add(new() { Reason = "Nome do arquivo contém termo", Evidence = term, Weight = 120 });
        foreach (var term in intent.Keywords.Take(4).Where(t => Contains(r.Title, t))) reasons.Add(new() { Reason = "Título contém termo principal", Evidence = term, Weight = 100 });
        foreach (var term in intent.Keywords.Take(4).Where(t => Contains(r.FolderName, t))) reasons.Add(new() { Reason = "Pasta relacionada ao contexto", Evidence = term, Weight = 30 });
        if (!string.IsNullOrWhiteSpace(intent.DocumentType) && Contains(r.DocumentType + " " + r.Title + " " + r.FileName, intent.DocumentType)) reasons.Add(new() { Reason = "Tipo documental compatível", Evidence = intent.DocumentType!, Weight = 15 });
        if (r.HasOcr) reasons.Add(new() { Reason = "OCR disponível", Evidence = "Trecho curto apresentado", Weight = 5 });
        return new SmartSearchResultItem { DocumentId = r.DocumentId, VersionId = r.VersionId, Title = r.Title, FileName = r.FileName, FolderName = r.FolderName, DocumentType = r.DocumentType, Classification = r.Classification, ClassificationName = r.Classification, PatientName = r.PatientName, Age = r.Age, Year = r.Year, OcrSnippet = MaskSensitive(TruncateSnippet(r.Snippet, 260)), Score = r.Score, HasOcr = r.HasOcr, Reasons = reasons };
    }

    private static async Task<bool> ExistsAsync(IDbConnection conn, string regclass, CancellationToken ct)
        => await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select to_regclass(@name)::text", new { name = regclass }, cancellationToken: ct)) is not null;

    private static async Task<IReadOnlyList<KeyValuePair<string, int>>> TopAsync(IDbConnection conn, string sql, Guid tenantId, CancellationToken ct)
        => (await conn.QueryAsync<TopRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).Select(x => new KeyValuePair<string, int>(string.IsNullOrWhiteSpace(x.K) ? "Não informado" : x.K, x.C)).ToList();

    private static string EscapeLike(string? value) => (value ?? string.Empty).Replace("%", "\\%").Replace("_", "\\_");
    private static bool Contains(string? source, string value) => (source ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
    private static string? TruncateSnippet(string? text, int max) => string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text : text[..max] + "…";
    private static string MaskSensitive(string? text) => string.IsNullOrWhiteSpace(text) ? string.Empty : Regex.Replace(Regex.Replace(text, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", "***.***.***-**"), @"\b\d{15}\b", "***************");
    private static string RedactSensitive(string text) => MaskSensitive(text);
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant();

    private const string SmartIndexSql = """
with ranked as (
select idx.document_id as "DocumentId", coalesce(idx.document_version_id, idx.version_id) as "VersionId", idx.title as "Title", idx.file_name as "FileName", idx.folder_name as "FolderName", idx.document_type as "DocumentType", idx.classification as "Classification", idx.patient_name as "PatientName", idx.extracted_age as "Age", idx.extracted_year as "Year", idx.search_text as "SearchText", idx.ocr_text as "Snippet", nullif(idx.ocr_text,'') is not null as "HasOcr",
(
case when @patientName is not null and idx.patient_name ilike @likePatientName then 40 else 0 end +
case when @year is not null and idx.extracted_year = @year then 15 else 0 end +
case when @age is not null and idx.extracted_age between @age - 1 and @age + 1 then 10 else 0 end +
case when @ageFrom is not null and idx.extracted_age between @ageFrom and @ageTo then 10 else 0 end +
case when @documentType is not null and coalesce(idx.document_type, idx.title, idx.file_name, '') ilike '%'||@documentType||'%' then 15 else 0 end +
case when @examType is not null and idx.search_text ilike '%'||@examType||'%' then 15 else 0 end +
case when @medicalRecordNumber is not null and idx.search_text ilike '%'||@medicalRecordNumber||'%' then 50 else 0 end +
case when @protocolNumber is not null and idx.search_text ilike '%'||@protocolNumber||'%' then 50 else 0 end +
case when @numericTerm is not null and coalesce(idx.file_name,'') ilike @likeNumericTerm then 150 else 0 end +
case when coalesce(idx.file_name,'') ilike @likeOriginalQuery then 120 else 0 end +
case when coalesce(idx.title,'') ilike @likeOriginalQuery then 100 else 0 end +
case when @numericTerm is not null and coalesce(idx.title,'') ilike @likeNumericTerm then 100 else 0 end +
case when coalesce(idx.search_text,'') ilike @likeOriginalQuery then 80 else 0 end +
case when @numericTerm is not null and coalesce(idx.ocr_text,'') ilike @likeNumericTerm then 70 else 0 end +
case when coalesce(idx.folder_name,'') ilike @likeOriginalQuery then 40 else 0 end +
case when @numericTerm is not null and coalesce(idx.folder_name,'') ilike @likeNumericTerm then 40 else 0 end +
case when nullif(idx.ocr_text,'') is not null then 5 else 0 end +
coalesce(ts_rank(idx.search_vector, plainto_tsquery('portuguese', @query)) * 40, 0) +
coalesce((select count(*) * 20 from unnest(@tokens::text[]) t where idx.search_text ilike '%'||t||'%'), 0)
)::numeric(10,2) as "Score"
from ged.document_search_index idx
join ged.document d on d.tenant_id=idx.tenant_id and d.id=idx.document_id
where idx.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and (@folderId is null or d.folder_id=@folderId)
/*DATE_FILTERS*/and (idx.search_vector @@ plainto_tsquery('portuguese', @query) or idx.search_text ilike @likeQuery or idx.file_name ilike @likeOriginalQuery or idx.title ilike @likeOriginalQuery or idx.document_id::text ilike @likeOriginalQuery or (@numericTerm is not null and (idx.file_name ilike @likeNumericTerm or idx.title ilike @likeNumericTerm or idx.search_text ilike @likeNumericTerm or idx.document_id::text ilike @likeNumericTerm)) or @patientName is not null and idx.patient_name ilike @likePatientName)
)
select *, count(*) over()::int as "TotalRows" from ranked order by "Score" desc, "Title" limit @limit offset @offset
""";

    private const string FallbackSql = """
with base as (
select d.id as "DocumentId", coalesce(v.id, ds.version_id) as "VersionId", coalesce(d.title, ds.file_name, v.file_name, 'Documento') as "Title", coalesce(v.file_name, ds.file_name) as "FileName", f.name as "FolderName", null::text as "DocumentType", null::text as "Classification", null::text as "PatientName", null::int as "Age", extract(year from coalesce(d.created_at, ds.updated_at))::int as "Year", concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text) as "SearchText", ds.ocr_text as "Snippet", nullif(ds.ocr_text,'') is not null as "HasOcr",
(case when concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text) ilike @likeQuery then 45 else 0 end +
case when coalesce(v.file_name, ds.file_name, '') ilike @likeOriginalQuery then 120 else 0 end +
case when coalesce(d.title,'') ilike @likeOriginalQuery then 100 else 0 end +
case when coalesce(ds.ocr_text,'') ilike @likeOriginalQuery then 70 else 0 end +
case when coalesce(f.name,'') ilike @likeOriginalQuery then 40 else 0 end + case when @year is not null and extract(year from coalesce(d.created_at, ds.updated_at))::int=@year then 15 else 0 end +
case when @numericTerm is not null and coalesce(v.file_name, ds.file_name, '') ilike @likeNumericTerm then 100 else 0 end +
case when @numericTerm is not null and coalesce(d.title,'') ilike @likeNumericTerm then 80 else 0 end +
case when @numericTerm is not null and coalesce(ds.ocr_text,'') ilike @likeNumericTerm then 50 else 0 end +
case when @numericTerm is not null and coalesce(f.name,'') ilike @likeNumericTerm then 20 else 0 end +
case when @numericTerm is not null and d.id::text ilike @likeNumericTerm then 55 else 0 end +
case when nullif(ds.ocr_text,'') is not null then 5 else 0 end +
coalesce((select count(*) * 20 from unnest(@tokens::text[]) t where concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text) ilike '%'||t||'%'), 0) + 20)::numeric(10,2) as "Score"
from ged.document d
left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id
left join ged.document_version v on v.tenant_id=d.tenant_id and (v.id=coalesce(ds.version_id, d.current_version_id) or v.id=d.current_version_id)
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and (@folderId is null or d.folder_id=@folderId)
/*DATE_FILTERS*/and (concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text, d.id::text) ilike @likeQuery or concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text, d.id::text) ilike @likeOriginalQuery or (@numericTerm is not null and concat_ws(' ', d.title, v.file_name, ds.file_name, f.name, ds.ocr_text, d.id::text) ilike @likeNumericTerm) or @query = '')
)
select *, count(*) over()::int as "TotalRows" from base order by "Score" desc, "Title" limit @limit offset @offset
""";


    private const string DirectSql = """
with base as (
select d.id as "DocumentId", v.id as "VersionId", coalesce(d.title, v.file_name, 'Documento') as "Title", v.file_name as "FileName", f.name as "FolderName", null::text as "DocumentType", null::text as "Classification", null::text as "PatientName", null::int as "Age", extract(year from d.created_at)::int as "Year", concat_ws(' ', d.title, v.file_name, f.name) as "SearchText", null::text as "Snippet", false as "HasOcr",
(case when coalesce(v.file_name,'') ilike @likeOriginalQuery then 120 else 0 end +
case when @numericTerm is not null and coalesce(v.file_name, '') ilike @likeNumericTerm then 150 else 0 end +
case when coalesce(d.title,'') ilike @likeOriginalQuery then 100 else 0 end +
case when coalesce(f.name,'') ilike @likeOriginalQuery then 50 else 0 end +
case when concat_ws(' ', d.title, v.file_name, f.name) ilike @likeQuery then 20 else 0 end +
case when d.created_at >= now() - interval '90 days' then 5 else 0 end +
coalesce((select count(*) * 20 from unnest(@tokens::text[]) t where concat_ws(' ', d.title, v.file_name, f.name) ilike '%'||t||'%'), 0) + 20)::numeric(10,2) as "Score"
from ged.document d
left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
where d.tenant_id=@tenantId and coalesce(d.reg_status,'A')='A' and (@folderId is null or d.folder_id=@folderId)
/*DATE_FILTERS*/and (concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike @likeQuery or concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike @likeOriginalQuery or (@numericTerm is not null and concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike @likeNumericTerm) or @query = '')
)
select *, count(*) over()::int as "TotalRows" from base order by "Score" desc, "Title" limit @limit offset @offset
""";

    private sealed class TopRow
    {
        public string K { get; set; } = string.Empty;
        public int C { get; set; }
    }

    private sealed class SearchRow
    {
        public Guid DocumentId { get; set; }
        public Guid? VersionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? FolderName { get; set; }
        public string? DocumentType { get; set; }
        public string? Classification { get; set; }
        public string? PatientName { get; set; }
        public int? Age { get; set; }
        public int? Year { get; set; }
        public string? SearchText { get; set; }
        public string? Snippet { get; set; }
        public bool HasOcr { get; set; }
        public decimal Score { get; set; }
        public int TotalRows { get; set; }
    }
}
