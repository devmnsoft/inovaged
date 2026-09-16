using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using DbDateTime = InovaGed.Application.Common.Database.PostgresDateTimeHelper;
using InovaGed.Application.SmartSearch;
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
        var useSmartIndex = hasSmartIndex && intent.Kind is not (SmartSearchIntentKind.WithoutOcr or SmartSearchIntentKind.WithoutIndex);
        var sql = intent.Kind == SmartSearchIntentKind.WithoutIndex ? DirectSql : useSmartIndex ? SmartIndexSql : fallbackSql;
        var isInventoryIntent = intent.Kind is SmartSearchIntentKind.WithoutOcr or SmartSearchIntentKind.WithoutIndex;
        var query = isInventoryIntent ? string.Empty : string.IsNullOrWhiteSpace(intent.ExpandedQuery) ? intent.OriginalQuery : intent.ExpandedQuery;
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
        p.Add("likeProtocolNumber", string.IsNullOrWhiteSpace(intent.ProtocolNumber) ? null : $"%{EscapeLike(intent.ProtocolNumber)}%", DbType.String);
        p.Add("exactPhrase", intent.ExactPhrases.FirstOrDefault(), DbType.String);
        p.Add("likeExactPhrase", intent.ExactPhrases.Count == 0 ? null : $"%{EscapeLike(intent.ExactPhrases[0])}%", DbType.String);
        p.Add("age", intent.Age, DbType.Int32);
        p.Add("ageFrom", intent.AgeFrom, DbType.Int32);
        p.Add("ageTo", intent.AgeTo, DbType.Int32);
        p.Add("year", intent.Year, DbType.Int32);
        p.Add("folderId", request.FolderId, DbType.Guid);
        var fromUtc = DbDateTime.ToUtc(intent.From);
        var toUtc = DbDateTime.ToUtc(intent.To);
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

        sql = sql.Replace("/*DATE_FILTERS*/", dateFilters.ToString())
            .Replace("/*INTENT_FILTER*/", BuildIntentFilter(intent.Kind, hasLegacyOcr, hasSmartIndex))
            .Replace("/*ORDER_BY*/", BuildOrderBy(request.Sort));
        fallbackSql = fallbackSql.Replace("/*INTENT_FILTER*/", BuildIntentFilter(intent.Kind, hasLegacyOcr, hasSmartIndex)).Replace("/*ORDER_BY*/", BuildOrderBy(request.Sort));
        p.Add("offset", offset, DbType.Int32);
        p.Add("limit", pageSize, DbType.Int32);

        var rows = (await conn.QueryAsync<SearchRow>(new CommandDefinition(sql, p, cancellationToken: ct, commandTimeout: 30))).ToList();
        var indexCount = hasSmartIndex ? rows.Count : 0;
        var fallbackCount = 0;
        if (useSmartIndex)
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

    public async Task<IReadOnlyList<SmartSearchSuggestion>> SuggestAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct)
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
union all
select query_text as "Text", 'Pesquisas salvas' as "Category"
from ged.smart_search_saved_search
where tenant_id=@tenantId and user_id=@userId and coalesce(reg_status,'A')='A'
  and (name ilike @like or query_text ilike @like)
order by 2, 1
limit 12
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return (await conn.QueryAsync<SmartSearchSuggestion>(new CommandDefinition(sql, new { tenantId, userId, like = $"%{EscapeLike(term)}%" }, cancellationToken: ct))).ToList();
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

    public async Task SaveFeedbackAsync(Guid tenantId, Guid userId, Guid documentId, string conversationId, bool helpful, CancellationToken ct)
    {
        const string sql = """
insert into ged.smart_search_feedback(tenant_id,user_id,document_id,conversation_key,helpful,created_at)
values (@tenantId,@userId,@documentId,@conversationId,@helpful,now())
on conflict (tenant_id,user_id,document_id,conversation_key)
do update set helpful=excluded.helpful,created_at=excluded.created_at
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, documentId, conversationId, helpful }, cancellationToken: ct));
    }

    public async Task SaveConversationTurnAsync(Guid tenantId, Guid userId, string conversationId, string question, DocumentAssistantResponse response, CancellationToken ct)
    {
        if (!Guid.TryParse(conversationId, out var id))
            throw new ArgumentException("Identificador de conversa inválido.", nameof(conversationId));
        const string sql = """
insert into ged.smart_search_conversation(id,tenant_id,user_id,title,created_at,updated_at,reg_status)
values(@id,@tenantId,@userId,left(@question,120),now(),now(),'A')
on conflict(id) do update set updated_at=now()
where ged.smart_search_conversation.tenant_id=@tenantId and ged.smart_search_conversation.user_id=@userId;
insert into ged.smart_search_message(tenant_id,conversation_id,role,content,intent_json,sources_json,created_at,reg_status)
select @tenantId,@id,'user',@question,null,'[]'::jsonb,now(),'A'
where exists(select 1 from ged.smart_search_conversation where id=@id and tenant_id=@tenantId and user_id=@userId);
insert into ged.smart_search_message(tenant_id,conversation_id,role,content,intent_json,sources_json,created_at,reg_status)
select @tenantId,@id,'assistant',@answer,cast(@intent as jsonb),cast(@sources as jsonb),now(),'A'
where exists(select 1 from ged.smart_search_conversation where id=@id and tenant_id=@tenantId and user_id=@userId);
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            id, tenantId, userId, question, response.Answer,
            intent = JsonSerializer.Serialize(response.AppliedCriteria),
            sources = JsonSerializer.Serialize(response.Sources.Select(x => new { x.DocumentId, x.Title, x.Relevance }))
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SmartSearchConversationSummary>> GetConversationHistoryAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = """
select c.id as "Id", coalesce(nullif(c.title,''),'Conversa sem título') as "Title", c.updated_at as "UpdatedAt",
       count(m.id)::int as "MessageCount"
from ged.smart_search_conversation c
left join ged.smart_search_message m on m.tenant_id=c.tenant_id and m.conversation_id=c.id and m.reg_status='A'
where c.tenant_id=@tenantId and c.user_id=@userId and c.reg_status='A'
group by c.id,c.title,c.updated_at order by c.updated_at desc limit 30
""";
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return (await conn.QueryAsync<SmartSearchConversationSummary>(new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct))).AsList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Histórico persistente do SmartSearch indisponível; verifique as migrations.");
            return [];
        }
    }

    public async Task<IReadOnlyList<SmartSearchSavedSearch>> GetSavedSearchesAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = """
select id as "Id", coalesce(name,'') as "Name", coalesce(query_text,'') as "Query",
       created_at as "CreatedAt", updated_at as "UpdatedAt", last_run_at as "LastRunAt",
       coalesce(run_count,0) as "RunCount", coalesce(is_favorite,false) as "IsFavorite"
from ged.smart_search_saved_search
where tenant_id = @tenantId and user_id = @userId and coalesce(reg_status,'A') = 'A'
order by is_favorite desc, created_at desc limit 50
""";
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SmartSearchSavedSearch>(new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<SmartSearchConversationMessage>> GetConversationMessagesAsync(Guid tenantId, Guid userId, Guid conversationId, CancellationToken ct)
    {
        const string sql = """
select m.id as "MessageId", m.conversation_id as "ConversationId",
       coalesce(m.role,'') as "Role", coalesce(m.content,'') as "Content",
       coalesce(m.sources_json,'[]'::jsonb)::text as "EvidenceJson",
       coalesce(m.intent_json,'{}'::jsonb)::text as "FiltersJson",
       m.created_at as "CreatedAt"
from ged.smart_search_message m
join ged.smart_search_conversation c on c.id=m.conversation_id and c.tenant_id=m.tenant_id
where m.tenant_id=@tenantId and c.user_id=@userId and c.id=@conversationId
  and c.reg_status='A' and m.reg_status='A'
order by m.created_at, m.id
limit 200
""";
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SmartSearchConversationMessage>(new CommandDefinition(sql,
            new { tenantId, userId, conversationId }, cancellationToken: ct))).AsList();
    }

    public async Task SaveSearchAsync(Guid tenantId, Guid userId, string name, string query, CancellationToken ct)
    {
        const string sql = """
insert into ged.smart_search_saved_search(tenant_id,user_id,name,query_text,created_at,updated_at,reg_status)
values(@tenantId,@userId,@name,@query,now(),now(),'A')
on conflict(tenant_id,user_id,query_hash) where reg_status='A'
do update set name=excluded.name,query_text=excluded.query_text,updated_at=now()
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, name, query }, cancellationToken: ct));
    }

    public async Task<bool> DeleteSavedSearchAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.smart_search_saved_search set reg_status='I',updated_at=now() where id=@id and tenant_id=@tenantId and user_id=@userId and coalesce(reg_status,'A')='A'", new { id, tenantId, userId }, cancellationToken: ct)) == 1;
    }

    public async Task<bool> RenameSavedSearchAsync(Guid tenantId, Guid userId, Guid id, string name, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.smart_search_saved_search set name=@name,updated_at=now() where id=@id and tenant_id=@tenantId and user_id=@userId and coalesce(reg_status,'A')='A'", new { id, tenantId, userId, name }, cancellationToken: ct)) == 1;
    }

    public async Task<bool> SetSavedSearchFavoriteAsync(Guid tenantId, Guid userId, Guid id, bool isFavorite, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.smart_search_saved_search set is_favorite=@isFavorite,updated_at=now() where id=@id and tenant_id=@tenantId and user_id=@userId and coalesce(reg_status,'A')='A'", new { id, tenantId, userId, isFavorite }, cancellationToken: ct)) == 1;
    }

    public async Task<string?> RunSavedSearchAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
    {
        const string sql = """
update ged.smart_search_saved_search
set last_run_at=now(),run_count=coalesce(run_count,0)+1,updated_at=now()
where id=@id and tenant_id=@tenantId and user_id=@userId and coalesce(reg_status,'A')='A'
returning query_text
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { id, tenantId, userId }, cancellationToken: ct));
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

    public async Task<SmartSearchAdminDashboard> GetAdminDashboardAsync(Guid tenantId, string section, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var model = new SmartSearchAdminDashboard { Section = section, Statistics = await GetStatisticsAsync(tenantId, ct) };
        if (await ExistsAsync(conn, "ged.search_synonym", ct))
            model.Synonyms = (await conn.QueryAsync<SmartSearchSynonymAdminRow>(new CommandDefinition("""
select id as "Id", term as "Term", synonym as "Synonym", coalesce(category,'business') as "Category",
       coalesce(weight,1) as "Weight", coalesce(reg_status,'A')='A' as "Active"
from ged.search_synonym where tenant_id=@tenantId order by lower(term), lower(synonym) limit 250
""", new { tenantId }, cancellationToken: ct))).ToList();
        if (await ExistsAsync(conn, "ged.smart_search_feedback", ct))
            model.NegativeFeedback = (await conn.QueryAsync<SmartSearchFeedbackAdminRow>(new CommandDefinition("""
select document_id as "DocumentId", conversation_key as "ConversationKey", created_at as "CreatedAt"
from ged.smart_search_feedback where tenant_id=@tenantId and helpful=false order by created_at desc limit 100
""", new { tenantId }, cancellationToken: ct))).ToList();
        return model;
    }

    public async Task SaveSynonymAsync(Guid tenantId, Guid? id, string term, string synonym, string category, decimal weight, bool active, CancellationToken ct)
    {
        const string sql = """
insert into ged.search_synonym(id,tenant_id,term,synonym,category,weight,reg_status)
values(coalesce(@id,gen_random_uuid()),@tenantId,@term,@synonym,@category,@weight,case when @active then 'A' else 'I' end)
on conflict(id) do update set term=excluded.term,synonym=excluded.synonym,category=excluded.category,
weight=excluded.weight,reg_status=excluded.reg_status where ged.search_synonym.tenant_id=@tenantId
""";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, id, term, synonym, category, weight, active }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SmartSearchRelatedDocument>> GetRelatedDocumentsAsync(Guid tenantId, Guid userId, Guid documentId, bool isAdmin, CancellationToken ct)
    {
        const string sql = """
select d.id as "DocumentId", coalesce(d.title,'Documento') as "Title",
       r.relationship_type as "RelationType",
       coalesce(nullif(r.evidence_summary,''), 'Relação documental cadastrada') as "Origin", true as "IsFormal"
from ged.document_relationship r
join ged.document d on d.tenant_id=r.tenant_id
 and d.id=case when r.document_id=@documentId then r.related_document_id else r.document_id end
where r.tenant_id=@tenantId and (r.document_id=@documentId or r.related_document_id=@documentId)
  and coalesce(r.reg_status,'A')='A' and coalesce(d.reg_status,'A')='A'
order by r.created_at_utc desc limit 20
""";
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SmartSearchRelatedDocument>(new CommandDefinition(sql, new { tenantId, userId, documentId, isAdmin }, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<SmartSearchComparisonDocument>> CompareDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, bool includeText, bool isAdmin, CancellationToken ct)
    {
        const string sql = """
select d.id as "DocumentId", coalesce(d.current_version_id,v.id) as "VersionId",
       coalesce(nullif(d.title,''),v.file_name,'Documento') as "Title", v.file_name as "FileName",
       dt.name as "DocumentType", coalesce(cp.name,cp.code) as "Classification", f.name as "Unit",
       null::text as "Protocol", d.status::text as "Status", d.created_at as "CreatedAt",
       case when v.id is null then null else (select count(*)::int from ged.document_version vx where vx.tenant_id=d.tenant_id and vx.document_id=d.id and coalesce(vx.created_at,vx.created_at_utc) <= coalesce(v.created_at,v.created_at_utc)) end as "VersionNumber",
       case when @includeText then left(ds.ocr_text,20000) else null end as "ExtractedText",
       nullif(ds.ocr_text,'') is not null as "HasExtractedText"
from ged.document d
left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.document_type_id
left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id=coalesce(dc.classification_id,d.classification_id)
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id and ds.version_id=coalesce(d.current_version_id,v.id)
where d.tenant_id=@tenantId and d.id=any(@documentIds) and coalesce(d.reg_status,'A')='A'
order by array_position(@documentIds,d.id)
""";
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SmartSearchComparisonDocument>(new CommandDefinition(sql, new { tenantId, userId, documentIds = documentIds.ToArray(), includeText, isAdmin }, cancellationToken: ct, commandTimeout: 20))).AsList();
    }

    public async Task<IReadOnlyList<SmartSearchCollection>> GetCollectionsAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        const string sql = """
select c.id as "Id", c.name as "Name", c.created_at as "CreatedAt", c.updated_at as "UpdatedAt", count(i.document_id)::int as "ItemCount"
from ged.smart_search_collection c left join ged.smart_search_collection_item i on i.collection_id=c.id and i.tenant_id=c.tenant_id and i.reg_status='A'
where c.tenant_id=@tenantId and c.user_id=@userId and c.reg_status='A'
group by c.id,c.name,c.created_at,c.updated_at order by c.updated_at desc limit 50
""";
        await using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<SmartSearchCollection>(new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct))).AsList();
    }

    public async Task<SmartSearchCollection?> GetCollectionAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var collection = await conn.QuerySingleOrDefaultAsync<SmartSearchCollection>(new CommandDefinition("select id as \"Id\",name as \"Name\",created_at as \"CreatedAt\",updated_at as \"UpdatedAt\" from ged.smart_search_collection where id=@id and tenant_id=@tenantId and user_id=@userId and reg_status='A'", new { id, tenantId, userId }, cancellationToken: ct));
        if (collection is null) return null;
        const string itemsSql = """
select i.document_id as "DocumentId", i.version_id as "VersionId", i.reference_mode as "ReferenceMode",
       coalesce(nullif(d.title,''),v.file_name,'Documento') as "Title", i.added_at as "AddedAt"
from ged.smart_search_collection_item i
join ged.document d on d.id=i.document_id and d.tenant_id=i.tenant_id and d.reg_status='A'
left join ged.document_version v on v.id=coalesce(i.version_id,d.current_version_id) and v.tenant_id=d.tenant_id
where i.collection_id=@id and i.tenant_id=@tenantId and i.reg_status='A' order by i.added_at desc limit 200
""";
        collection.Items = (await conn.QueryAsync<SmartSearchCollectionItem>(new CommandDefinition(itemsSql, new { id, tenantId }, cancellationToken: ct))).AsList();
        collection.ItemCount = collection.Items.Count;
        return collection;
    }

    public async Task<Guid> CreateCollectionAsync(Guid tenantId, Guid userId, string name, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("insert into ged.smart_search_collection(tenant_id,user_id,name) values(@tenantId,@userId,@name) returning id", new { tenantId, userId, name }, cancellationToken: ct));
    }

    public async Task<bool> RenameCollectionAsync(Guid tenantId, Guid userId, Guid id, string name, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.smart_search_collection set name=@name,updated_at=now(),revision=revision+1 where id=@id and tenant_id=@tenantId and user_id=@userId and reg_status='A'", new { tenantId, userId, id, name }, cancellationToken: ct)) == 1;
    }

    public async Task<bool> ArchiveCollectionAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.smart_search_collection set reg_status='I',updated_at=now(),revision=revision+1 where id=@id and tenant_id=@tenantId and user_id=@userId and reg_status='A'", new { tenantId, userId, id }, cancellationToken: ct)) == 1;
    }

    public async Task<SmartSearchCollectionMutation> AddCollectionItemsAsync(Guid tenantId, Guid userId, Guid id, IReadOnlyCollection<Guid> documentIds, CancellationToken ct)
    {
        var requested = documentIds.Distinct().Take(50).ToArray();
        const string sql = """
with owned as (select id from ged.smart_search_collection where id=@id and tenant_id=@tenantId and user_id=@userId and reg_status='A'),
allowed as (select d.id,d.current_version_id from ged.document d,owned where d.tenant_id=@tenantId and d.id=any(@documentIds) and d.reg_status='A'),
inserted as (insert into ged.smart_search_collection_item(tenant_id,collection_id,document_id,version_id,reference_mode,added_by)
 select @tenantId,@id,id,null,'CURRENT',@userId from allowed
 on conflict(tenant_id,collection_id,document_id) do update set reg_status='A',removed_at=null,added_at=now(),added_by=@userId returning document_id)
update ged.smart_search_collection set updated_at=now(),revision=revision+1 where id=@id and tenant_id=@tenantId and user_id=@userId
returning (select count(*) from inserted)::int
""";
        await using var conn = await _db.OpenAsync(ct);
        var added = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { tenantId, userId, id, documentIds = requested }, cancellationToken: ct)) ?? 0;
        return new SmartSearchCollectionMutation { Requested = requested.Length, Added = added, Skipped = requested.Length - added };
    }

    public async Task<bool> RemoveCollectionItemAsync(Guid tenantId, Guid userId, Guid id, Guid documentId, CancellationToken ct)
    {
        const string sql = """
update ged.smart_search_collection_item i set reg_status='I',removed_at=now()
where i.tenant_id=@tenantId and i.collection_id=@id and i.document_id=@documentId and i.reg_status='A'
and exists(select 1 from ged.smart_search_collection c where c.id=i.collection_id and c.tenant_id=i.tenant_id and c.user_id=@userId and c.reg_status='A')
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, id, documentId }, cancellationToken: ct)) == 1;
    }

    private static SmartSearchResultItem Map(SearchRow r, SmartSearchIntent intent)
    {
        var reasons = new List<SmartSearchResultReason>();
        if (!string.IsNullOrWhiteSpace(intent.ProtocolNumber) && Contains(r.Title + " " + r.FileName, intent.ProtocolNumber)) reasons.Add(new() { Reason = "Protocolo exato", Evidence = intent.ProtocolNumber, Weight = 500 });
        foreach (var phrase in intent.ExactPhrases.Where(phrase => Contains(r.Title + " " + r.SearchText, phrase))) reasons.Add(new() { Reason = "Frase exata", Evidence = phrase, Weight = 240 });
        if (!string.IsNullOrWhiteSpace(intent.PatientName) && Contains(r.PatientName + " " + r.SearchText, intent.PatientName)) reasons.Add(new() { Reason = "Nome parecido", Evidence = intent.PatientName!, Weight = 40 });
        if (intent.Year.HasValue && r.Year == intent.Year) reasons.Add(new() { Reason = "Ano/período compatível", Evidence = intent.Year.Value.ToString(), Weight = 15 });
        if (intent.Age.HasValue && r.Age.HasValue && Math.Abs(r.Age.Value - intent.Age.Value) <= 1) reasons.Add(new() { Reason = "Idade aproximada", Evidence = r.Age.Value.ToString(), Weight = 10 });
        foreach (var term in intent.ClinicalTerms.Take(6).Where(t => Contains(r.SearchText, t))) reasons.Add(new() { Reason = r.HasOcr ? "OCR menciona termo relacionado" : "Metadados mencionam termo relacionado", Evidence = term, Weight = 80 });
        foreach (var term in intent.Keywords.Take(4).Where(t => Contains(r.FileName, t))) reasons.Add(new() { Reason = "Nome do arquivo contém termo", Evidence = term, Weight = 120 });
        foreach (var term in intent.Keywords.Take(4).Where(t => Contains(r.Title, t))) reasons.Add(new() { Reason = "Título contém termo principal", Evidence = term, Weight = 100 });
        foreach (var term in intent.Keywords.Take(4).Where(t => Contains(r.FolderName, t))) reasons.Add(new() { Reason = "Pasta relacionada ao contexto", Evidence = term, Weight = 30 });
        if (!string.IsNullOrWhiteSpace(intent.DocumentType) && Contains(r.DocumentType + " " + r.Title + " " + r.FileName, intent.DocumentType)) reasons.Add(new() { Reason = "Tipo documental compatível", Evidence = intent.DocumentType!, Weight = 15 });
        if (r.HasOcr) reasons.Add(new() { Reason = "OCR disponível", Evidence = "Trecho curto apresentado", Weight = 5 });
        return new SmartSearchResultItem { DocumentId = r.DocumentId, VersionId = r.VersionId, Title = r.Title, FileName = r.FileName, FolderName = r.FolderName, DocumentType = r.DocumentType, Classification = r.Classification, ClassificationName = r.Classification, PatientName = r.PatientName, Age = r.Age, Year = r.Year, OcrSnippet = MaskSensitive(SelectSnippet(r.Snippet, intent, 260)), Score = r.Score, HasOcr = r.HasOcr, Reasons = reasons };
    }

    private static async Task<bool> ExistsAsync(IDbConnection conn, string regclass, CancellationToken ct)
        => await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select to_regclass(@name)::text", new { name = regclass }, cancellationToken: ct)) is not null;

    private static async Task<IReadOnlyList<KeyValuePair<string, int>>> TopAsync(IDbConnection conn, string sql, Guid tenantId, CancellationToken ct)
        => (await conn.QueryAsync<TopRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).Select(x => new KeyValuePair<string, int>(string.IsNullOrWhiteSpace(x.K) ? "Não informado" : x.K, x.C)).ToList();

    private static string EscapeLike(string? value) => (value ?? string.Empty).Replace("%", "\\%").Replace("_", "\\_");
    private static string BuildIntentFilter(SmartSearchIntentKind kind, bool hasLegacyOcr, bool hasSmartIndex) => kind switch
    {
        SmartSearchIntentKind.WithoutOcr when hasLegacyOcr => "and nullif(ds.ocr_text,'') is null\n",
        SmartSearchIntentKind.WithoutIndex when hasSmartIndex => "and not exists (select 1 from ged.document_search_index ssi where ssi.tenant_id=d.tenant_id and ssi.document_id=d.id)\n",
        _ => string.Empty
    };
    private static bool Contains(string? source, string value) => (source ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
    private static string BuildOrderBy(string? sort) => (sort ?? string.Empty).ToLowerInvariant() switch
    {
        "newest" => "\"Year\" desc nulls last, \"Title\", \"DocumentId\"",
        "oldest" => "\"Year\" asc nulls last, \"Title\", \"DocumentId\"",
        "title" => "lower(\"Title\"), \"DocumentId\"",
        _ => "\"Score\" desc, lower(\"Title\"), \"DocumentId\""
    };
    private static string? SelectSnippet(string? text, SmartSearchIntent intent, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var terms = intent.ExactPhrases.Concat(intent.Keywords).Concat(intent.ClinicalTerms).Where(x => x.Length >= 2);
        var index = terms.Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, index - max / 3);
        var length = Math.Min(max, text.Length - start);
        return $"{(start > 0 ? "…" : string.Empty)}{text.Substring(start, length).ReplaceLineEndings(" ").Trim()}{(start + length < text.Length ? "…" : string.Empty)}";
    }
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
case when @protocolNumber is not null and (idx.title ilike @likeProtocolNumber or idx.file_name ilike @likeProtocolNumber) then 500 else 0 end +
case when @protocolNumber is not null and idx.search_text ilike @likeProtocolNumber then 180 else 0 end +
case when @exactPhrase is not null and idx.title ilike @likeExactPhrase then 240 else 0 end +
case when @exactPhrase is not null and idx.search_text ilike @likeExactPhrase then 110 else 0 end +
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
/*DATE_FILTERS*//*INTENT_FILTER*/and (idx.search_vector @@ plainto_tsquery('portuguese', @query) or idx.search_text ilike @likeQuery or idx.file_name ilike @likeOriginalQuery or idx.title ilike @likeOriginalQuery or idx.document_id::text ilike @likeOriginalQuery or (@numericTerm is not null and (idx.file_name ilike @likeNumericTerm or idx.title ilike @likeNumericTerm or idx.search_text ilike @likeNumericTerm or idx.document_id::text ilike @likeNumericTerm)) or (@patientName is not null and idx.patient_name ilike @likePatientName) or exists (select 1 from unnest(@tokens::text[]) t where idx.search_text ilike '%'||t||'%') or @query = '')
)
select *, count(*) over()::int as "TotalRows" from ranked order by /*ORDER_BY*/ limit @limit offset @offset
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
/*DATE_FILTERS*//*INTENT_FILTER*/and (concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text, d.id::text) ilike @likeQuery or concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text, d.id::text) ilike @likeOriginalQuery or (@numericTerm is not null and concat_ws(' ', d.title, v.file_name, ds.file_name, f.name, ds.ocr_text, d.id::text) ilike @likeNumericTerm) or exists (select 1 from unnest(@tokens::text[]) t where concat_ws(' ', d.title, v.file_name, f.name, ds.ocr_text, d.id::text) ilike '%'||t||'%') or @query = '')
)
select *, count(*) over()::int as "TotalRows" from base order by /*ORDER_BY*/ limit @limit offset @offset
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
/*DATE_FILTERS*//*INTENT_FILTER*/and (concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike @likeQuery or concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike @likeOriginalQuery or (@numericTerm is not null and concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike @likeNumericTerm) or exists (select 1 from unnest(@tokens::text[]) t where concat_ws(' ', d.title, v.file_name, f.name, d.id::text) ilike '%'||t||'%') or @query = '')
)
select *, count(*) over()::int as "TotalRows" from base order by /*ORDER_BY*/ limit @limit offset @offset
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
