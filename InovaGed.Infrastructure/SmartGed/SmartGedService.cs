using System.Diagnostics;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using InovaGed.Application.SmartGed;

namespace InovaGed.Infrastructure.SmartGed;

public sealed class SmartGedService : IDocumentIntelligenceService, IDocumentClassificationSuggestionService, IDocumentRetentionSuggestionService, ISmartGedSearchService
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentMetadataExtractor _extractor;
    private readonly IAuditWriter _audit;
    private readonly IRetentionJobRepository _retentionJobs;

    public SmartGedService(IDbConnectionFactory db, IDocumentMetadataExtractor extractor, IAuditWriter audit, IRetentionJobRepository retentionJobs)
    {
        _db = db;
        _extractor = extractor;
        _audit = audit;
        _retentionJobs = retentionJobs;
    }

    public async Task<Guid> AnalyzeDocumentAsync(Guid tenantId, Guid documentId, Guid? userId, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        var document = await c.QuerySingleOrDefaultAsync<DocumentTextRow>(new CommandDefinition("select id as Id,coalesce(title,'Documento sem título') as Title from ged.document where tenant_id=@tenantId and id=@documentId and coalesce(reg_status,'A')='A'", new { tenantId, documentId }, cancellationToken: ct));
        if (document is null) throw new InvalidOperationException("Documento não encontrado no tenant informado.");
        var hasSearch = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.document_search') is not null", cancellationToken: ct));
        var text = document.Title;
        if (hasSearch)
        {
            var columns = (await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='document_search'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var textColumn = columns.Contains("ocr_text") ? "ocr_text" : columns.Contains("content") ? "content" : columns.Contains("search_text") ? "search_text" : null;
            if (textColumn is not null && columns.Contains("document_id")) text += "\n" + await c.ExecuteScalarAsync<string?>(new CommandDefinition($"select {textColumn} from ged.document_search where tenant_id=@tenantId and document_id=@documentId limit 1", new { tenantId, documentId }, cancellationToken: ct));
        }
        var extraction = await _extractor.ExtractAsync(new(text, document.Title), ct);
        var id = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_ai_analysis(id,tenant_id,document_id,analysis_status,analysis_source,extracted_text,extracted_summary,detected_document_type,detected_subject,detected_date,detected_identifiers,detected_sensitive_data,confidence,analyzed_at)
values(@id,@tenantId,@documentId,'COMPLETED','LOCAL_RULES',@text,@Summary,@DocumentType,@Subject,@DetectedDate,cast(@identifiers as jsonb),cast(@sensitive as jsonb),@Confidence,now())
""", new { id, tenantId, documentId, text, extraction.Summary, extraction.DocumentType, extraction.Subject, DetectedDate = extraction.DetectedDate?.ToDateTime(TimeOnly.MinValue), identifiers = JsonSerializer.Serialize(extraction.Identifiers), sensitive = JsonSerializer.Serialize(extraction.SensitiveIndicators), extraction.Confidence }, cancellationToken: ct));
        await CreateSuggestionsAsync(c, tenantId, documentId, id, extraction, ct);
        await Audit(tenantId, userId, "DOCUMENT_AI_ANALYZED", documentId, new { analysisId = id, source = "LOCAL_RULES", extraction.Confidence }, ct);
        return id;
    }

    private async Task CreateSuggestionsAsync(Npgsql.NpgsqlConnection c, Guid tenantId, Guid documentId, Guid analysisId, DocumentMetadataExtractionResult extraction, CancellationToken ct)
    {
        var planReady = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.classification_plan') is not null", cancellationToken: ct));
        if (planReady)
        {
            List<PlanRow> plans;
            try
            {
                plans = (await c.QueryAsync<PlanRow>(new CommandDefinition("""
select coalesce(i.classification_id, cp.id) as Id,
       coalesce(i.code, cp.code) as Code,
       coalesce(i.name, cp.title) as Title,
       coalesce(cp.description, '') as Description,
       coalesce(i.final_destination, cp.final_destination) as FinalDestination
from ged.classification_plan_version_item i
join ged.classification_plan_version v on v.tenant_id=i.tenant_id and v.id=i.version_id
left join ged.classification_plan cp on cp.tenant_id=i.tenant_id and cp.id=i.classification_id
where i.tenant_id=@tenantId and coalesce(i.is_active,true) and coalesce(v.reg_status,'A')='A'
  and v.version_no=(select max(version_no) from ged.classification_plan_version where tenant_id=@tenantId and coalesce(reg_status,'A')='A')
order by i.code limit 500
""", new { tenantId }, cancellationToken: ct))).ToList();
            }
            catch
            {
                plans = [];
            }

            if (plans.Count == 0)
            {
                plans = (await c.QueryAsync<PlanRow>(new CommandDefinition("select id as Id,code as Code,title as Title,description as Description,final_destination as FinalDestination from ged.classification_plan where tenant_id=@tenantId and coalesce(reg_status,'A')='A' limit 500", new { tenantId }, cancellationToken: ct))).ToList();
            }

            var best = plans.Select(p => new { Plan = p, Score = extraction.Keywords.Count(k => ($"{p.Code} {p.Title} {p.Description}").Contains(k, StringComparison.OrdinalIgnoreCase)) }).OrderByDescending(x => x.Score).FirstOrDefault();
            if (best is { Score: > 0 })
            {
                var confidence = Math.Min(95, 45 + best.Score * 15);
                await c.ExecuteAsync(new CommandDefinition("insert into ged.document_classification_suggestion(tenant_id,document_id,analysis_id,suggested_classification_id,suggested_classification_code,suggested_classification_title,suggested_reason,confidence) values(@tenantId,@documentId,@analysisId,@Id,@Code,@Title,@reason,@confidence)", new { tenantId, documentId, analysisId, best.Plan.Id, best.Plan.Code, best.Plan.Title, reason = $"Correspondência local de {best.Score} palavra(s)-chave.", confidence }, cancellationToken: ct));
                await Audit(tenantId, null, "CLASSIFICATION_SUGGESTION_CREATED", documentId, new { analysisId, best.Plan.Id, confidence }, ct);
                await c.ExecuteAsync(new CommandDefinition("insert into ged.document_retention_suggestion(tenant_id,document_id,analysis_id,suggested_phase,suggested_final_destination,suggested_reason,confidence,status) values(@tenantId,@documentId,@analysisId,'CORRENTE',@destination,'Derivada da classificação sugerida; exige confirmação humana.',@confidence,'PENDING')", new { tenantId, documentId, analysisId, destination = best.Plan.FinalDestination ?? "REQUER_REVISAO", confidence = confidence - 10 }, cancellationToken: ct));
                await Audit(tenantId, null, "RETENTION_SUGGESTION_CREATED", documentId, new { analysisId }, ct);
            }
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition("insert into ged.document_quality_issue(tenant_id,document_id,issue_type,severity,title,recommended_action) values(@tenantId,@documentId,'CLASSIFICATION_PLAN_UNAVAILABLE','HIGH','Plano de classificação indisponível para sugestão automática.','Execute a migration de compatibilidade do plano de classificação.')", new { tenantId, documentId }, cancellationToken: ct));
            await Audit(tenantId, null, "DOCUMENT_QUALITY_ISSUE_CREATED", documentId, new { type = "CLASSIFICATION_PLAN_UNAVAILABLE" }, ct);
        }
        if (extraction.Confidence < 55) await c.ExecuteAsync(new CommandDefinition("insert into ged.document_quality_issue(tenant_id,document_id,issue_type,severity,title,recommended_action) values(@tenantId,@documentId,'LOW_CONFIDENCE','MEDIUM','Análise com baixa confiança','Revise OCR e metadados do documento.')", new { tenantId, documentId }, cancellationToken: ct));
    }

    public async Task<DocumentIntelligenceDetails?> GetAnalysisAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        var a = await c.QuerySingleOrDefaultAsync<AnalysisRow>(new CommandDefinition("select id as Id,document_id as DocumentId,analysis_status as Status,extracted_summary as Summary,detected_document_type as DocumentType,detected_subject as Subject,detected_date as DetectedDate,detected_identifiers::text as IdentifiersJson,detected_sensitive_data::text as SensitiveJson,confidence as Confidence from ged.document_ai_analysis where tenant_id=@tenantId and document_id=@documentId and reg_status='A' order by created_at desc limit 1", new { tenantId, documentId }, cancellationToken: ct));
        if (a is null) return null;
        var classification = (await ListClassifications(c, tenantId, "and analysis_id=@analysisId", new { tenantId, analysisId = a.Id }, ct)).FirstOrDefault();
        var retention = (await ListRetentions(c, tenantId, "and analysis_id=@analysisId", new { tenantId, analysisId = a.Id }, ct)).FirstOrDefault();
        var issues = (await c.QueryAsync<IssueRow>(new CommandDefinition("select id as Id,document_id as DocumentId,issue_type as Type,severity as Severity,title as Title,recommended_action as RecommendedAction,status as Status from ged.document_quality_issue where tenant_id=@tenantId and document_id=@documentId and reg_status='A' order by created_at desc", new { tenantId, documentId }, cancellationToken: ct))).Select(Map).ToArray();
        return new(a.Id, a.DocumentId, a.Status, a.Summary, a.DocumentType, a.Subject, a.DetectedDate.HasValue ? DateOnly.FromDateTime(a.DetectedDate.Value) : null, DeserializeDictionary(a.IdentifiersJson), DeserializeList(a.SensitiveJson), a.Confidence, classification, retention, issues);
    }

    public async Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListPendingAsync(Guid tenantId, CancellationToken ct) { await using var c = await _db.OpenAsync(ct); return await ListClassifications(c, tenantId, "and status='PENDING'", new { tenantId }, ct); }
    async Task<IReadOnlyList<DocumentRetentionSuggestionItem>> IDocumentRetentionSuggestionService.ListPendingAsync(Guid tenantId, CancellationToken ct) { await using var c = await _db.OpenAsync(ct); return await ListRetentions(c, tenantId, "and status='PENDING'", new { tenantId }, ct); }
    public Task AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct) => ReviewClassification(tenantId, suggestionId, userId, "ACCEPTED", notes, ct);
    public Task RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct) => ReviewClassification(tenantId, suggestionId, userId, "REJECTED", reason, ct);
    Task IDocumentRetentionSuggestionService.AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct) => ReviewRetention(tenantId, suggestionId, userId, "ACCEPTED", notes, ct);
    Task IDocumentRetentionSuggestionService.RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct) => ReviewRetention(tenantId, suggestionId, userId, "REJECTED", reason, ct);

    private async Task ReviewClassification(Guid tenantId, Guid id, Guid userId, string status, string? notes, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var suggestion = await c.QuerySingleOrDefaultAsync<AcceptedClassificationRow>(new CommandDefinition("update ged.document_classification_suggestion set status=@status,reviewed_by=@userId,reviewed_at=now(),review_notes=@notes where tenant_id=@tenantId and id=@id and status='PENDING' returning document_id as DocumentId,suggested_classification_id as ClassificationId,confidence as Confidence", new { tenantId, id, userId, status, notes }, tx, cancellationToken: ct));
        if (suggestion is null) throw new InvalidOperationException("Sugestão não encontrada ou já revisada.");
        if (status == "ACCEPTED" && suggestion.ClassificationId.HasValue)
        {
            await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_classification(tenant_id,document_id,classification_id,confidence,suggestion_factors,reclassification_reason,source,classified_by)
values(@tenantId,@DocumentId,@ClassificationId,@confidence,cast(@factors as jsonb),@notes,'SMART_GED_CONFIRMED',@userId)
""", new { tenantId, suggestion.DocumentId, suggestion.ClassificationId, confidence = suggestion.Confidence / 100m, factors = JsonSerializer.Serialize(new { suggestionId = id, humanConfirmed = true }), notes, userId }, tx, cancellationToken: ct));

            await c.ExecuteAsync(new CommandDefinition("""
update ged.document d
set classification_id=@ClassificationId,
    classification_version_id=coalesce((
        select v.id from ged.classification_plan_version v
        join ged.classification_plan_version_item i on i.tenant_id=v.tenant_id and i.version_id=v.id
        where v.tenant_id=@tenantId and i.classification_id=@ClassificationId and coalesce(i.is_active,true) and coalesce(v.reg_status,'A')='A'
        order by v.version_no desc limit 1
    ), d.classification_version_id),
    updated_at=now(),
    updated_by=@userId
where d.tenant_id=@tenantId and d.id=@DocumentId
""", new { tenantId, suggestion.DocumentId, suggestion.ClassificationId, userId }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);

        if (status == "ACCEPTED" && suggestion.ClassificationId.HasValue)
        {
            try
            {
                await _retentionJobs.EnqueueRecalculateAsync(tenantId, suggestion.DocumentId, "SMART_GED_CONFIRMED", ct);
            }
            catch
            {
                // Non-blocking if retention queue table is unavailable
            }
        }

        await Audit(tenantId, userId, $"CLASSIFICATION_SUGGESTION_{status}", suggestion.DocumentId, new { id, notes }, ct);
    }

    private async Task ReviewRetention(Guid tenantId, Guid id, Guid userId, string status, string? notes, CancellationToken ct) { await using var c = await _db.OpenAsync(ct); var doc = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("update ged.document_retention_suggestion set status=@status,reviewed_by=@userId,reviewed_at=now(),review_notes=@notes where tenant_id=@tenantId and id=@id and status='PENDING' returning document_id", new { tenantId, id, userId, status, notes }, cancellationToken: ct)); if (!doc.HasValue) throw new InvalidOperationException("Sugestão não encontrada ou já revisada."); await Audit(tenantId,userId,$"RETENTION_SUGGESTION_{status}",doc,new{id,notes},ct); }

    public async Task<SmartGedSearchResult> SearchAsync(SmartGedSearchQuery query, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        await using var c = await _db.OpenAsync(ct);
        var term = (query.Text ?? string.Empty).Trim();
        if (term.Length < 2 && query.FolderId is null && query.ClassificationId is null && query.TypeId is null)
            return new(term, [], 0);

        var sql = new System.Text.StringBuilder("""
select a.document_id as DocumentId, coalesce(d.title, 'Documento') as Document, a.extracted_summary as Summary,
coalesce(pvi.code || ' - ' || pvi.name, cp.code || ' - ' || cp.title, cs.suggested_classification_code || ' - ' || cs.suggested_classification_title, 'Não classificado') as Classification,
case when exists(select 1 from ged.document_quality_issue qi where qi.tenant_id=a.tenant_id and qi.document_id=a.document_id and qi.status='OPEN') then 'ATENÇÃO' else 'OK' end as QualityStatus,
left(regexp_replace(coalesce(a.extracted_text, a.extracted_summary, ''), '\s+', ' ', 'g'), 240) as Excerpt,
v.version_number as VersionNumber,
d.retention_status as RetentionStatus
from ged.document_ai_analysis a
join ged.document d on d.id = a.document_id and d.tenant_id = a.tenant_id
left join ged.document_version v on v.id = d.current_version_id
left join ged.classification_plan cp on cp.id = d.classification_id and cp.tenant_id = d.tenant_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id = d.tenant_id and pvi.version_id = d.classification_version_id and pvi.classification_id = d.classification_id
left join lateral (
    select suggested_classification_code, suggested_classification_title
    from ged.document_classification_suggestion x
    where x.tenant_id = a.tenant_id and x.document_id = a.document_id
    order by created_at desc limit 1
) cs on true
where a.tenant_id = @TenantId
  and a.reg_status = 'A'
  and coalesce(d.reg_status, 'A') = 'A'
  and coalesce(d.status, '') <> 'DELETED'
""");

        var dp = new DynamicParameters();
        dp.Add("TenantId", query.TenantId);
        dp.Add("limit", Math.Clamp(query.Limit, 1, 100));

        if (!string.IsNullOrWhiteSpace(term))
        {
            sql.Append(" and (coalesce(d.title, '') ilike @pattern or coalesce(a.extracted_text, '') ilike @pattern or coalesce(a.extracted_summary, '') ilike @pattern or coalesce(pvi.code, cp.code, cs.suggested_classification_code, '') ilike @pattern or coalesce(pvi.name, cp.title, cs.suggested_classification_title, '') ilike @pattern)");
            dp.Add("pattern", $"%{term}%");
        }
        if (query.FolderId.HasValue)
        {
            sql.Append(" and d.folder_id = @FolderId");
            dp.Add("FolderId", query.FolderId.Value);
        }
        if (query.ClassificationId.HasValue)
        {
            sql.Append(" and d.classification_id = @ClassificationId");
            dp.Add("ClassificationId", query.ClassificationId.Value);
        }
        if (query.TypeId.HasValue)
        {
            sql.Append(" and d.type_id = @TypeId");
            dp.Add("TypeId", query.TypeId.Value);
        }
        if (query.CreatedAfter.HasValue)
        {
            sql.Append(" and d.created_at >= @CreatedAfter");
            dp.Add("CreatedAfter", query.CreatedAfter.Value);
        }
        if (query.CreatedBefore.HasValue)
        {
            sql.Append(" and d.created_at <= @CreatedBefore");
            dp.Add("CreatedBefore", query.CreatedBefore.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.RetentionStatus))
        {
            sql.Append(" and d.retention_status = @RetentionStatus");
            dp.Add("RetentionStatus", query.RetentionStatus);
        }

        sql.Append(" order by a.created_at desc limit @limit");

        var rows = await c.QueryAsync<SearchRow>(new CommandDefinition(sql.ToString(), dp, cancellationToken: ct));
        var items = rows.Select(x => new SmartGedSearchItem(x.DocumentId, x.Document, x.Summary, x.Classification, null, x.QualityStatus, x.Excerpt, x.VersionNumber, x.RetentionStatus)).ToArray();
        clock.Stop();

        await c.ExecuteAsync(new CommandDefinition("insert into ged.smart_search_query_log(tenant_id,user_id,query_text,normalized_query,result_count,execution_ms,payload_json) values(@TenantId,@UserId,@Text,@normalized,@count,@ms,cast(@payload as jsonb))", new { query.TenantId, query.UserId, query.Text, normalized = term.ToLowerInvariant(), count = items.Length, ms = (int)clock.ElapsedMilliseconds, payload = JsonSerializer.Serialize(new { limit = query.Limit, query.FolderId, query.ClassificationId, query.TypeId }) }, cancellationToken: ct));
        await Audit(query.TenantId, query.UserId, "SMART_SEARCH_EXECUTED", null, new { query = term, resultCount = items.Length }, ct);
        return new(term, items, (int)clock.ElapsedMilliseconds);
    }

    private static async Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListClassifications(Npgsql.NpgsqlConnection c, Guid tenantId, string filter, object args, CancellationToken ct) => (await c.QueryAsync<ClassificationRow>(new CommandDefinition($"select id as Id,document_id as DocumentId,suggested_classification_code as Code,suggested_classification_title as Title,suggested_reason as Reason,confidence as Confidence,status as Status from ged.document_classification_suggestion where tenant_id=@tenantId and reg_status='A' {filter} order by confidence desc", args, cancellationToken:ct))).Select(x=>new DocumentClassificationSuggestionItem(x.Id,x.DocumentId,x.Code,x.Title,x.Reason,x.Confidence,x.Status)).ToArray();
    private static async Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListRetentions(Npgsql.NpgsqlConnection c, Guid tenantId, string filter, object args, CancellationToken ct) => (await c.QueryAsync<RetentionRow>(new CommandDefinition($"select id as Id,document_id as DocumentId,suggested_phase as Phase,suggested_final_destination as FinalDestination,suggested_trigger_event as TriggerEvent,suggested_retention_until as RetentionUntil,suggested_reason as Reason,confidence as Confidence,status as Status from ged.document_retention_suggestion where tenant_id=@tenantId and reg_status='A' {filter} order by confidence desc",args,cancellationToken:ct))).Select(x=>new DocumentRetentionSuggestionItem(x.Id,x.DocumentId,x.Phase,x.FinalDestination,x.TriggerEvent,x.RetentionUntil.HasValue?DateOnly.FromDateTime(x.RetentionUntil.Value):null,x.Reason,x.Confidence,x.Status)).ToArray();
    private Task Audit(Guid tenantId, Guid? userId,string action,Guid? documentId,object data,CancellationToken ct)=>_audit.WriteAsync(tenantId,userId,action,"SMART_GED",documentId,"Ação de inteligência documental",null,null,data,ct);
    private static IReadOnlyDictionary<string,IReadOnlyList<string>> DeserializeDictionary(string? json)=>string.IsNullOrWhiteSpace(json)?new Dictionary<string,IReadOnlyList<string>>():JsonSerializer.Deserialize<Dictionary<string,IReadOnlyList<string>>>(json)??new();
    private static IReadOnlyList<string> DeserializeList(string? json)=>string.IsNullOrWhiteSpace(json)?[]:JsonSerializer.Deserialize<string[]>(json)??[];
    private static DocumentQualityIssueItem Map(IssueRow x)=>new(x.Id,x.DocumentId,x.Type,x.Severity,x.Title,x.RecommendedAction,x.Status);
    private sealed class DocumentTextRow { public Guid Id {get;set;} public string Title {get;set;}=""; }
    private sealed class PlanRow { public Guid Id {get;set;} public string? Code {get;set;} public string? Title {get;set;} public string? Description {get;set;} public string? FinalDestination {get;set;} }
    private sealed class AnalysisRow { public Guid Id {get;set;} public Guid DocumentId {get;set;} public string Status {get;set;}=""; public string? Summary {get;set;} public string? DocumentType {get;set;} public string? Subject {get;set;} public DateTime? DetectedDate {get;set;} public string? IdentifiersJson {get;set;} public string? SensitiveJson {get;set;} public decimal Confidence {get;set;} }
    private sealed class AcceptedClassificationRow { public Guid DocumentId {get;set;} public Guid? ClassificationId {get;set;} public decimal Confidence {get;set;} }
    private sealed class ClassificationRow { public Guid Id {get;set;} public Guid DocumentId {get;set;} public string? Code {get;set;} public string? Title {get;set;} public string? Reason {get;set;} public decimal Confidence {get;set;} public string Status {get;set;}=""; }
    private sealed class RetentionRow { public Guid Id {get;set;} public Guid DocumentId {get;set;} public string? Phase {get;set;} public string? FinalDestination {get;set;} public string? TriggerEvent {get;set;} public DateTime? RetentionUntil {get;set;} public string? Reason {get;set;} public decimal Confidence {get;set;} public string Status {get;set;}=""; }
    private sealed class IssueRow { public Guid Id {get;set;} public Guid DocumentId {get;set;} public string Type {get;set;}=""; public string Severity {get;set;}=""; public string Title {get;set;}=""; public string? RecommendedAction {get;set;} public string Status {get;set;}=""; }
    private sealed class SearchRow { public Guid DocumentId {get;set;} public string Document {get;set;}=""; public string? Summary {get;set;} public string? Classification {get;set;} public string QualityStatus {get;set;}=""; public string Excerpt {get;set;}=""; public int? VersionNumber {get;set;} public string? RetentionStatus {get;set;} }
}
