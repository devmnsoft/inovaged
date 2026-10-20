using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using InovaGed.Application.SmartGed;
using InovaGed.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SmartGed;

public sealed class SmartGedService : IDocumentIntelligenceService, IDocumentClassificationSuggestionService, IDocumentRetentionSuggestionService, ISmartGedReviewRecoveryService, ISmartGedSearchService
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentMetadataExtractor _extractor;
    private readonly IAuditWriter _audit;
    private readonly IRetentionJobRepository _retentionJobs;
    private readonly IDocumentClassificationCommands _commands;
    private readonly IAbacAuthorizationService _authorization;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly ILogger<SmartGedService> _logger;

    public SmartGedService(
        IDbConnectionFactory db,
        IDocumentMetadataExtractor extractor,
        IAuditWriter audit,
        IRetentionJobRepository retentionJobs,
        IDocumentClassificationCommands commands,
        IAbacAuthorizationService authorization,
        IGedAccessPolicyService accessPolicy,
        ILogger<SmartGedService> logger)
    {
        _db = db;
        _extractor = extractor;
        _audit = audit;
        _retentionJobs = retentionJobs;
        _commands = commands;
        _authorization = authorization;
        _accessPolicy = accessPolicy;
        _logger = logger;
    }

    public async Task<Guid> AnalyzeDocumentAsync(Guid tenantId, Guid documentId, Guid? userId, CancellationToken ct)
    {
        if (userId.HasValue)
        {
            var canAnalyze = await _authorization.CanAccessDocumentAsync(tenantId, userId.Value, documentId, "CLASSIFY", new Dictionary<string, string>(), ct)
                          || await _authorization.CanAccessDocumentAsync(tenantId, userId.Value, documentId, "EDIT", new Dictionary<string, string>(), ct);
            if (!canAnalyze)
                throw new UnauthorizedAccessException("Usuário não possui autorização para analisar este documento.");
        }

        await using var c = await _db.OpenAsync(ct);
        var document = await c.QuerySingleOrDefaultAsync<DocumentHeaderRow>(new CommandDefinition(
            "select id as Id, coalesce(title,'Documento sem título') as Title, current_version_id as CurrentVersionId from ged.document where tenant_id=@tenantId and id=@documentId and coalesce(reg_status,'A')='A'",
            new { tenantId, documentId }, cancellationToken: ct));
        if (document is null) throw new InvalidOperationException("Documento não encontrado no tenant informado.");

        var hasSearch = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.document_search') is not null", cancellationToken: ct));
        var text = document.Title;
        if (hasSearch)
        {
            var columns = (await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='document_search'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var textColumn = columns.Contains("ocr_text") ? "ocr_text" : columns.Contains("content") ? "content" : columns.Contains("search_text") ? "search_text" : null;
            if (textColumn is not null && columns.Contains("document_id"))
            {
                var searchContent = await c.ExecuteScalarAsync<string?>(new CommandDefinition($"select {textColumn} from ged.document_search where tenant_id=@tenantId and document_id=@documentId limit 1", new { tenantId, documentId }, cancellationToken: ct));
                if (!string.IsNullOrWhiteSpace(searchContent)) text += "\n" + searchContent;
            }
        }

        var extraction = await _extractor.ExtractAsync(new(text, document.Title), ct);
        var id = Guid.NewGuid();

        var aiCols = (await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='document_ai_analysis'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasVersionCol = aiCols.Contains("version_id");

        if (hasVersionCol)
        {
            await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_ai_analysis(id,tenant_id,document_id,version_id,analysis_status,analysis_source,extracted_text,extracted_summary,detected_document_type,detected_subject,detected_date,detected_identifiers,detected_sensitive_data,confidence,analyzed_at)
values(@id,@tenantId,@documentId,@CurrentVersionId,'COMPLETED','LOCAL_RULES',@text,@Summary,@DocumentType,@Subject,@DetectedDate,cast(@identifiers as jsonb),cast(@sensitive as jsonb),@Confidence,now())
""", new { id, tenantId, documentId, CurrentVersionId = document.CurrentVersionId, text, extraction.Summary, extraction.DocumentType, extraction.Subject, DetectedDate = extraction.DetectedDate?.ToDateTime(TimeOnly.MinValue), identifiers = JsonSerializer.Serialize(extraction.Identifiers), sensitive = JsonSerializer.Serialize(extraction.SensitiveIndicators), extraction.Confidence }, cancellationToken: ct));
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_ai_analysis(id,tenant_id,document_id,analysis_status,analysis_source,extracted_text,extracted_summary,detected_document_type,detected_subject,detected_date,detected_identifiers,detected_sensitive_data,confidence,analyzed_at)
values(@id,@tenantId,@documentId,'COMPLETED','LOCAL_RULES',@text,@Summary,@DocumentType,@Subject,@DetectedDate,cast(@identifiers as jsonb),cast(@sensitive as jsonb),@Confidence,now())
""", new { id, tenantId, documentId, text, extraction.Summary, extraction.DocumentType, extraction.Subject, DetectedDate = extraction.DetectedDate?.ToDateTime(TimeOnly.MinValue), identifiers = JsonSerializer.Serialize(extraction.Identifiers), sensitive = JsonSerializer.Serialize(extraction.SensitiveIndicators), extraction.Confidence }, cancellationToken: ct));
        }

        await CreateSuggestionsAsync(c, tenantId, documentId, document.CurrentVersionId, id, extraction, ct);
        await Audit(tenantId, userId, "DOCUMENT_AI_ANALYZED", documentId, new { analysisId = id, versionId = document.CurrentVersionId, source = "LOCAL_RULES", extraction.Confidence }, ct);
        return id;
    }

    private async Task CreateSuggestionsAsync(Npgsql.NpgsqlConnection c, Guid tenantId, Guid documentId, Guid? versionId, Guid analysisId, DocumentMetadataExtractionResult extraction, CancellationToken ct)
    {
        var planReady = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.classification_plan') is not null", cancellationToken: ct));
        if (planReady)
        {
            var maxVersionRow = await c.QuerySingleOrDefaultAsync<PlanVersionInfoRow>(new CommandDefinition("""
select id as Id, version_no as VersionNo
from ged.classification_plan_version
where tenant_id=@tenantId and coalesce(reg_status,'A')='A' and published_at is not null
order by published_at desc, version_no desc limit 1
""", new { tenantId }, cancellationToken: ct));

            List<PlanRow> plans = [];
            Guid? planVersionId = null;

            if (maxVersionRow is not null)
            {
                planVersionId = maxVersionRow.Id;
                // Versão vigente do plano de classificação
                plans = (await c.QueryAsync<PlanRow>(new CommandDefinition("""
select coalesce(i.classification_id, cp.id) as Id,
       i.id as ItemId,
       coalesce(i.code, cp.code) as Code,
       coalesce(i.name, cp.name) as Title,
       coalesce(cp.description, '') as Description,
       coalesce(i.final_destination, cp.final_destination) as FinalDestination,
       i.retention_start_event::text as TriggerEvent,
       i.retention_active_days as ActiveDays,
       i.retention_active_months as ActiveMonths,
       i.retention_active_years as ActiveYears,
       i.retention_archive_days as ArchiveDays,
       i.retention_archive_months as ArchiveMonths,
       i.retention_archive_years as ArchiveYears
from ged.classification_plan_version_item i
join ged.classification_plan_version v on v.tenant_id=i.tenant_id and v.id=i.version_id
left join ged.classification_plan cp on cp.tenant_id=i.tenant_id and cp.id=i.classification_id
where i.tenant_id=@tenantId and coalesce(i.is_active,true) and coalesce(v.reg_status,'A')='A'
  and v.version_no=@maxVersion
order by i.code limit 500
""", new { tenantId, maxVersion = maxVersionRow.VersionNo }, cancellationToken: ct))).ToList();

                if (plans.Count == 0)
                {
                    await c.ExecuteAsync(new CommandDefinition("insert into ged.document_quality_issue(tenant_id,document_id,issue_type,severity,title,recommended_action) values(@tenantId,@documentId,'NO_ELIGIBLE_CLASSES','MEDIUM','Plano de classificação vigente sem classes ativas.','Cadastre ou ative classes na versão vigente do plano de classificação.')", new { tenantId, documentId }, cancellationToken: ct));
                    return;
                }
            }
            else
            {
                // Instalação legada reconhecida (sem versões publicadas)
                plans = (await c.QueryAsync<PlanRow>(new CommandDefinition("""
select id as Id, code as Code, name as Title, coalesce(description, '') as Description, final_destination as FinalDestination
from ged.classification_plan
where tenant_id=@tenantId and coalesce(reg_status,'A')='A' and coalesce(is_active, true)
order by code limit 500
""", new { tenantId }, cancellationToken: ct))).ToList();
            }

            var best = plans.Select(p => new { Plan = p, Score = extraction.Keywords.Count(k => ($"{p.Code} {p.Title} {p.Description}").Contains(k, StringComparison.OrdinalIgnoreCase)) }).OrderByDescending(x => x.Score).FirstOrDefault();
            if (best is { Score: > 0 })
            {
                var confidence = Math.Min(95, 45 + best.Score * 15);

                var classCols = (await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='document_classification_suggestion'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (classCols.Contains("version_id"))
                {
                    await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_classification_suggestion(tenant_id,document_id,analysis_id,version_id,plan_version_id,suggested_classification_id,suggested_classification_code,suggested_classification_title,suggested_reason,confidence)
values(@tenantId,@documentId,@analysisId,@versionId,@planVersionId,@Id,@Code,@Title,@reason,@confidence)
""", new { tenantId, documentId, analysisId, versionId, planVersionId, best.Plan.Id, best.Plan.Code, best.Plan.Title, reason = $"Correspondência local de {best.Score} palavra(s)-chave.", confidence }, cancellationToken: ct));
                }
                else
                {
                    await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_classification_suggestion(tenant_id,document_id,analysis_id,suggested_classification_id,suggested_classification_code,suggested_classification_title,suggested_reason,confidence)
values(@tenantId,@documentId,@analysisId,@Id,@Code,@Title,@reason,@confidence)
""", new { tenantId, documentId, analysisId, best.Plan.Id, best.Plan.Code, best.Plan.Title, reason = $"Correspondência local de {best.Score} palavra(s)-chave.", confidence }, cancellationToken: ct));
                }

                await Audit(tenantId, null, "CLASSIFICATION_SUGGESTION_CREATED", documentId, new { analysisId, versionId, best.Plan.Id, confidence }, ct);

                var retCols = (await c.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='document_retention_suggestion'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var retentionIsActionable = versionId.HasValue && planVersionId.HasValue && best.Plan.HasCanonicalRetentionRule();
                var retentionReason = retentionIsActionable
                    ? "Derivada do item versionado do plano de classificação; exige confirmação humana antes do recálculo canônico."
                    : "Pendência: a temporalidade não possui regra/versionamento comprovado para aplicação automática. Confirme a classificação e reanalise o documento.";
                var retentionStatus = "PENDING";
                var destination = best.Plan.FinalDestination;
                var phase = retentionIsActionable ? "CORRENTE" : null;
                var ruleId = retentionIsActionable ? best.Plan.ItemId : (Guid?)null;
                var triggerEvent = retentionIsActionable ? best.Plan.TriggerEvent : null;
                if (retCols.Contains("version_id"))
                {
                    await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_retention_suggestion(tenant_id,document_id,analysis_id,version_id,plan_version_id,suggested_rule_id,suggested_phase,suggested_final_destination,suggested_trigger_event,suggested_reason,confidence,status)
values(@tenantId,@documentId,@analysisId,@versionId,@planVersionId,@ruleId,@phase,@destination,@triggerEvent,@retentionReason,@confidence,@retentionStatus)
""", new { tenantId, documentId, analysisId, versionId, planVersionId, ruleId, phase, destination, triggerEvent, retentionReason, confidence = confidence - 10, retentionStatus }, cancellationToken: ct));
                }
                else
                {
                    await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_retention_suggestion(tenant_id,document_id,analysis_id,suggested_phase,suggested_final_destination,suggested_reason,confidence,status)
values(@tenantId,@documentId,@analysisId,@phase,@destination,@retentionReason,@confidence,@retentionStatus)
""", new { tenantId, documentId, analysisId, phase, destination, retentionReason, confidence = confidence - 10, retentionStatus }, cancellationToken: ct));
                }

                await Audit(tenantId, null, "RETENTION_SUGGESTION_CREATED", documentId, new { analysisId, versionId, planVersionId, ruleId, triggerEvent, status = retentionStatus }, ct);
            }
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition("insert into ged.document_quality_issue(tenant_id,document_id,issue_type,severity,title,recommended_action) values(@tenantId,@documentId,'CLASSIFICATION_PLAN_UNAVAILABLE','HIGH','Plano de classificação indisponível para sugestão automática.','Execute a migration de compatibilidade do plano de classificação.')", new { tenantId, documentId }, cancellationToken: ct));
            await Audit(tenantId, null, "DOCUMENT_QUALITY_ISSUE_CREATED", documentId, new { type = "CLASSIFICATION_PLAN_UNAVAILABLE" }, ct);
        }

        if (extraction.Confidence < 55)
            await c.ExecuteAsync(new CommandDefinition("insert into ged.document_quality_issue(tenant_id,document_id,issue_type,severity,title,recommended_action) values(@tenantId,@documentId,'LOW_CONFIDENCE','MEDIUM','Análise com baixa confiança','Revise OCR e metadados do documento.')", new { tenantId, documentId }, cancellationToken: ct));
    }

    public async Task<DocumentIntelligenceDetails?> GetAnalysisAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        var a = await c.QuerySingleOrDefaultAsync<AnalysisRow>(new CommandDefinition("select id as Id,document_id as DocumentId,analysis_status as Status,extracted_summary as Summary,detected_document_type as DocumentType,detected_subject as Subject,detected_date as DetectedDate,detected_identifiers::text as IdentifiersJson,detected_sensitive_data::text as SensitiveJson,confidence as Confidence from ged.document_ai_analysis where tenant_id=@tenantId and document_id=@documentId and reg_status='A' order by created_at desc limit 1", new { tenantId, documentId }, cancellationToken: ct));
        if (a is null) return null;
        var classification = (await ListClassifications(c, tenantId, "and s.analysis_id=@analysisId", new { tenantId, analysisId = a.Id }, ct)).FirstOrDefault();
        var retention = (await ListRetentions(c, tenantId, "and s.analysis_id=@analysisId", new { tenantId, analysisId = a.Id }, ct)).FirstOrDefault();
        var issues = (await c.QueryAsync<IssueRow>(new CommandDefinition("select id as Id,document_id as DocumentId,issue_type as Type,severity as Severity,title as Title,recommended_action as RecommendedAction,status as Status from ged.document_quality_issue where tenant_id=@tenantId and document_id=@documentId and reg_status='A' order by created_at desc", new { tenantId, documentId }, cancellationToken: ct))).Select(Map).ToArray();
        return new(a.Id, a.DocumentId, a.Status, a.Summary, a.DocumentType, a.Subject, a.DetectedDate.HasValue ? DateOnly.FromDateTime(a.DetectedDate.Value) : null, DeserializeDictionary(a.IdentifiersJson), DeserializeList(a.SensitiveJson), a.Confidence, classification, retention, issues);
    }

    public async Task<DocumentQualityIssueList> ListOpenIssuesAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        var available = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
    select 1
    from information_schema.tables
    where table_schema = 'ged' and table_name = 'document_quality_issue')
""", cancellationToken: ct));
        if (!available) return new([], false);

        var args = new DynamicParameters();
        args.Add("tenantId", tenantId);
        args.Add("userId", userId);
        args.Add("permissionCode", "Documents.View");
        args.Add("write", false);
        args.Add("hour", DateTime.UtcNow.Hour);
        var rows = await c.QueryAsync<QualityIssueListRow>(new CommandDefinition($"""
select qi.id as Id,
       qi.document_id as DocumentId,
       d.title as DocumentTitle,
       qi.issue_type as Type,
       qi.severity as Severity,
       qi.title as Title,
       qi.recommended_action as RecommendedAction,
       qi.status as Status,
       qi.created_at as CreatedAt
from ged.document_quality_issue qi
join ged.document d on d.id = qi.document_id and d.tenant_id = qi.tenant_id
where qi.tenant_id = @tenantId
  and qi.status = 'OPEN'
  and qi.reg_status = 'A'
  and {AbacAuthorizationService.DocumentAccessPredicate("d")}
order by case qi.severity when 'HIGH' then 1 when 'MEDIUM' then 2 else 3 end, qi.created_at desc
limit 200
""", args, cancellationToken: ct));
        return new(rows.Select(x => new DocumentQualityIssueListItem(
            x.Id, x.DocumentId, x.DocumentTitle, x.Type, x.Severity, x.Title, x.RecommendedAction, x.Status, x.CreatedAt)).ToArray(), true);
    }

    public Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListPendingAsync(Guid tenantId, CancellationToken ct) =>
        ListPendingAsync(tenantId, null, ct);

    public Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListPendingAsync(Guid tenantId, Guid? userId, CancellationToken ct) =>
        ListAsync(tenantId, "PENDING", userId, ct);

    public Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListAsync(Guid tenantId, string? status, CancellationToken ct) =>
        ListAsync(tenantId, status, null, ct);

    public async Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListAsync(Guid tenantId, string? status, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return [];
        await using var c = await _db.OpenAsync(ct);
        var filter = string.IsNullOrWhiteSpace(status) || string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase)
            ? ""
            : "and s.status = @status";
        var items = await ListClassifications(c, tenantId, filter, new { tenantId, status }, ct);
        if (items.Count == 0) return items;

        var docIds = items.Select(x => x.DocumentId).Distinct().ToArray();
        var authorized = await _authorization.FilterDocumentsAsync(tenantId, userId.Value, docIds, "VIEW", ct);
        return items.Where(x => authorized.Contains(x.DocumentId)).ToArray();
    }

    public async Task<SmartGedSuggestionPage<DocumentClassificationSuggestionItem>> ListPageAsync(Guid tenantId, string? status, Guid? userId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 50);
        if (!userId.HasValue) return new([], 0, page, pageSize);

        await using var c = await _db.OpenAsync(ct);
        return await ListClassificationPage(c, tenantId, NormalizeReviewStatus(status), userId.Value, page, pageSize, ct);
    }

    public async Task<int> CountAsync(Guid tenantId, string? status, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return 0;
        await using var c = await _db.OpenAsync(ct);
        return await CountClassificationReviews(c, tenantId, NormalizeReviewStatus(status), userId.Value, ct);
    }

    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> IDocumentRetentionSuggestionService.ListPendingAsync(Guid tenantId, CancellationToken ct) =>
        ((IDocumentRetentionSuggestionService)this).ListPendingAsync(tenantId, null, ct);

    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> IDocumentRetentionSuggestionService.ListPendingAsync(Guid tenantId, Guid? userId, CancellationToken ct) =>
        ((IDocumentRetentionSuggestionService)this).ListAsync(tenantId, "PENDING", userId, ct);

    Task<IReadOnlyList<DocumentRetentionSuggestionItem>> IDocumentRetentionSuggestionService.ListAsync(Guid tenantId, string? status, CancellationToken ct) =>
        ((IDocumentRetentionSuggestionService)this).ListAsync(tenantId, status, null, ct);

    async Task<IReadOnlyList<DocumentRetentionSuggestionItem>> IDocumentRetentionSuggestionService.ListAsync(Guid tenantId, string? status, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return [];
        await using var c = await _db.OpenAsync(ct);
        var filter = string.IsNullOrWhiteSpace(status) || string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase)
            ? ""
            : "and s.status = @status";
        var items = await ListRetentions(c, tenantId, filter, new { tenantId, status }, ct);
        if (items.Count == 0) return items;

        var docIds = items.Select(x => x.DocumentId).Distinct().ToArray();
        var authorized = await _authorization.FilterDocumentsAsync(tenantId, userId.Value, docIds, "VIEW", ct);
        return items.Where(x => authorized.Contains(x.DocumentId)).ToArray();
    }

    async Task<SmartGedSuggestionPage<DocumentRetentionSuggestionItem>> IDocumentRetentionSuggestionService.ListPageAsync(Guid tenantId, string? status, Guid? userId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 50);
        if (!userId.HasValue) return new([], 0, page, pageSize);

        await using var c = await _db.OpenAsync(ct);
        return await ListRetentionPage(c, tenantId, NormalizeReviewStatus(status), userId.Value, page, pageSize, ct);
    }

    async Task<int> IDocumentRetentionSuggestionService.CountAsync(Guid tenantId, string? status, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return 0;
        await using var c = await _db.OpenAsync(ct);
        return await CountRetentionReviews(c, tenantId, NormalizeReviewStatus(status), userId.Value, ct);
    }

    public Task AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct) => ReviewClassification(tenantId, suggestionId, userId, "ACCEPTED", notes, ct);
    public Task RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct) => ReviewClassification(tenantId, suggestionId, userId, "REJECTED", reason, ct);
    Task IDocumentRetentionSuggestionService.AcceptAsync(Guid tenantId, Guid suggestionId, Guid userId, string? notes, CancellationToken ct) => ReviewRetention(tenantId, suggestionId, userId, "ACCEPTED", notes, ct);
    Task IDocumentRetentionSuggestionService.RejectAsync(Guid tenantId, Guid suggestionId, Guid userId, string reason, CancellationToken ct) => ReviewRetention(tenantId, suggestionId, userId, "REJECTED", reason, ct);

    private async Task ReviewClassification(Guid tenantId, Guid id, Guid userId, string status, string? notes, CancellationToken ct)
    {
        notes = NormalizeReviewText(notes);
        if (status == "REJECTED") RequireReviewReason(notes);
        await using var c = await _db.OpenAsync(ct);
        var columns = await LoadSuggestionColumns(c, "document_classification_suggestion", ct);

        var existing = await c.QuerySingleOrDefaultAsync<ClassificationSuggestionDetailRow>(new CommandDefinition($"""
select s.id as Id, s.document_id as DocumentId, s.suggested_classification_id as ClassificationId,
       s.confidence as Confidence, s.status as Status,
       {SuggestionColumnSql.Uuid(columns, "version_id")} as VersionId, {SuggestionColumnSql.Uuid(columns, "plan_version_id")} as PlanVersionId,
       {SuggestionColumnSql.LegacyUnproven(columns)} as IsLegacyUnproven,
       {SuggestionColumnSql.Uuid(columns, "review_operation_id")} as ReviewOperationId
from ged.document_classification_suggestion s
where s.tenant_id = @tenantId and s.id = @id
""", new { tenantId, id }, cancellationToken: ct));

        if (existing is null) throw new InvalidOperationException("Sugestão não encontrada.");

        // 1. Validar autorização documental no servidor tanto para ACEITAR quanto para REJEITAR
        var allowed = await _authorization.CanAccessDocumentAsync(tenantId, userId, existing.DocumentId, "CLASSIFY", new Dictionary<string, string>(), ct);
        if (!allowed)
            throw new UnauthorizedAccessException("Usuário não possui autorização para classificar este documento.");

        // Idempotência só depois da autorização: estado existente não pode virar oráculo de acesso.
        if (string.Equals(existing.Status, status, StringComparison.OrdinalIgnoreCase))
            return;

        await EnsureReviewableAsync(c, tenantId, existing.Status, existing.ReviewOperationId, "CLASSIFICATION", existing.Id, existing.DocumentId, ct);

        if (status == "REJECTED")
        {
            var updated = await c.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion
set status = 'REJECTED', reviewed_by = @userId, reviewed_at = now(), review_notes = @notes
where tenant_id = @tenantId and id = @id and status in ('PENDING','FAILED')
""", new { tenantId, id, userId, notes }, cancellationToken: ct));

            if (updated == 0)
                throw new InvalidOperationException("Sugestão já revisada ou não pendente.");

            await Audit(tenantId, userId, "CLASSIFICATION_SUGGESTION_REJECTED", existing.DocumentId, new { id, notes }, ct);
            return;
        }

        if (status == "ACCEPTED")
        {
            if (!existing.ClassificationId.HasValue)
                throw new InvalidOperationException("Sugestão sem classe associada não pode ser aceita.");
            if (existing.IsLegacyUnproven || !existing.VersionId.HasValue || !existing.PlanVersionId.HasValue || !SuggestionColumnSql.CanApplyReview(columns))
                throw new InvalidOperationException("Sugestão legada ou sem origem/versionamento comprovado deve ser reanalisada antes da aplicação.");

            var operationId = Guid.NewGuid();
            var applicationCompleted = false;
            var claimed = await c.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion s
set status = 'PROCESSING',
    reviewed_by = @userId,
    reviewed_at = now(),
    review_notes = @notes,
    review_operation_id = @operationId,
    processing_started_at = now(),
    processing_expires_at = now() + interval '15 minutes',
    failed_at = null,
    failure_code = null,
    public_failure_message = null
from ged.document d
where s.tenant_id = @tenantId
  and s.id = @id
  and s.status in ('PENDING','FAILED')
  and d.tenant_id = s.tenant_id
  and d.id = s.document_id
  and coalesce(d.reg_status,'A') = 'A'
  and coalesce(d.status,'') <> 'DELETED'
  and (s.version_id is null or d.current_version_id is null or s.version_id = d.current_version_id)
""", new { tenantId, id, userId, notes, operationId }, cancellationToken: ct));

            if (claimed == 0)
                throw new InvalidOperationException("Sugestão já revisada concorrentemente.");

            // 2. Proteger contra sugestão obsoleta e validar documento ativo
            try
            {
                var docState = await c.QuerySingleOrDefaultAsync<DocumentCurrentStateRow>(new CommandDefinition("""
select id as Id, coalesce(status,'') as DocStatus, coalesce(reg_status,'A') as RegStatus,
       classification_id as CurrentClassificationId, current_version_id as CurrentVersionId,
       xmin::text::bigint as Token
from ged.document
where tenant_id = @tenantId and id = @DocumentId
""", new { tenantId, DocumentId = existing.DocumentId }, cancellationToken: ct));

                if (docState is null || docState.RegStatus != "A" || docState.DocStatus == "DELETED")
                    throw new InvalidOperationException("Documento não encontrado ou inativo.");

                if (!docState.CurrentVersionId.HasValue)
                    throw new InvalidOperationException("Documento sem versão vigente não pode receber recomendação assistida.");

                if (existing.VersionId.Value != docState.CurrentVersionId.Value)
                {
                    throw new InvalidOperationException("A sugestão refere-se a uma versão documental anterior à atual. É necessária uma nova análise.");
                }

                // 3. Validar classe e versão elegíveis
                var currentPlanVersionId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select " + PlanVersionSql.CurrentId("@tenantId"),
                    new { tenantId }, cancellationToken: ct));

                if (currentPlanVersionId.HasValue)
                {
                    var isEligible = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
    select 1
    from ged.classification_plan_version_item i
    join ged.classification_plan_version v on v.tenant_id = i.tenant_id and v.id = i.version_id
    where i.tenant_id = @tenantId
      and i.classification_id = @ClassificationId
      and coalesce(i.is_active, true)
      and coalesce(v.reg_status, 'A') = 'A'
      and v.id = @currentPlanVersionId
)
""", new { tenantId, ClassificationId = existing.ClassificationId.Value, currentPlanVersionId }, cancellationToken: ct));

                    if (!isEligible)
                        throw new InvalidOperationException("A classe sugerida não pertence à versão vigente do plano de classificação ou está inativa.");
                }

                // 4. Aplicar classificação pelo comando canônico existente
                var saveCommand = new SaveManualIntegratedCommand(
                    TenantId: tenantId,
                    DocumentId: existing.DocumentId,
                    UserId: userId,
                    ClassificationAction: ClassificationEditAction.Replace,
                    ClassificationId: existing.ClassificationId,
                    ConfirmClassificationRemoval: false,
                    TypeAction: DocumentTypeEditAction.Keep,
                    DocumentTypeId: null,
                    HasTags: false,
                    Tags: null,
                    HasMetadata: false,
                    Metadata: null,
                    IsSystemExecution: false,
                    SystemExecutionReason: "SMART_GED_CONFIRMED",
                    ExpectedDocumentToken: docState.Token
                );

                var saveResult = await _commands.SaveManualIntegratedAsync(saveCommand, ct);
                if (!saveResult.Success)
                    throw new InvalidOperationException(saveResult.Message);
                applicationCompleted = true;
                await UpsertReviewOperationResultAsync(
                    c,
                    tenantId,
                    operationId,
                    "CLASSIFICATION",
                    id,
                    existing.DocumentId,
                    existing.VersionId,
                    existing.PlanVersionId,
                    existing.ClassificationId,
                    null,
                    "EFFECT_CONFIRMED",
                    "EFFECT_CONFIRMED",
                    true,
                    new { classificationId = existing.ClassificationId, retentionRecalculated = saveResult.RetentionRecalculated },
                    CancellationToken.None);

                // 5. Atualizar EXCLUSIVAMENTE a sugestão revisada (não aprova temporalidade indiscriminadamente)
                var rowsUpdated = await c.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion
set status = 'ACCEPTED',
    reviewed_by = @userId,
    reviewed_at = now(),
    review_notes = @notes,
    applied_at = now(),
    processing_expires_at = null,
    failed_at = null,
    failure_code = null,
    public_failure_message = null
where tenant_id = @tenantId and id = @id and status = 'PROCESSING' and review_operation_id = @operationId;
""", new { tenantId, id, userId, notes, operationId }, cancellationToken: CancellationToken.None));

                if (rowsUpdated == 0)
                    throw new InvalidOperationException("Sugestão já revisada concorrentemente.");

                try
                {
                    await Audit(tenantId, userId, "CLASSIFICATION_SUGGESTION_ACCEPTED", existing.DocumentId, new
                    {
                        id,
                        operationId,
                        classificationId = existing.ClassificationId,
                        retentionRecalculated = saveResult.RetentionRecalculated,
                        notes
                    }, CancellationToken.None);
                    await MarkReviewOperationAuditedAsync(c, tenantId, operationId, "CLASSIFICATION_SUGGESTION_ACCEPTED", CancellationToken.None);
                }
                catch (Exception auditEx)
                {
                    // A decisão já foi persistida; falha de auditoria deve ser tratada operacionalmente sem desfazer a decisão.
                    _logger.LogError(auditEx, "Falha de auditoria após aceite de classificação assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId}", tenantId, id, operationId);
                }
            }
            catch (Exception ex)
            {
                await UpsertReviewOperationResultAsync(
                    c,
                    tenantId,
                    operationId,
                    "CLASSIFICATION",
                    id,
                    existing.DocumentId,
                    existing.VersionId,
                    existing.PlanVersionId,
                    existing.ClassificationId,
                    null,
                    applicationCompleted ? "EFFECT_CONFIRMED" : "INDETERMINATE",
                    applicationCompleted ? "FAILED_AFTER_EFFECT" : "INDETERMINATE",
                    applicationCompleted,
                    new { failure = ex.GetType().Name, message = PublicReviewFailure(ex), effectConfirmation = applicationCompleted ? "CONFIRMED" : "UNKNOWN" },
                    CancellationToken.None);
                await MarkClassificationReviewFailedAsync(c, tenantId, id, operationId, applicationCompleted, ex, CancellationToken.None);
                _logger.LogWarning(ex, "Falha na revisão de classificação assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId} Applied={Applied}", tenantId, id, operationId, applicationCompleted);
                throw;
            }
        }
    }

    private async Task ReviewRetention(Guid tenantId, Guid id, Guid userId, string status, string? notes, CancellationToken ct)
    {
        notes = NormalizeReviewText(notes);
        if (status == "REJECTED") RequireReviewReason(notes);
        await using var c = await _db.OpenAsync(ct);
        var columns = await LoadSuggestionColumns(c, "document_retention_suggestion", ct);
        var existing = await c.QuerySingleOrDefaultAsync<RetentionSuggestionDetailRow>(new CommandDefinition($"""
select s.id as Id, s.document_id as DocumentId, s.suggested_rule_id as RuleId,
       s.confidence as Confidence, s.status as Status, {SuggestionColumnSql.Uuid(columns, "version_id")} as VersionId,
       {SuggestionColumnSql.Uuid(columns, "plan_version_id")} as PlanVersionId,
       pvi.classification_id as SuggestedClassificationId,
       pvi.retention_start_event::text as TriggerEvent,
       pvi.final_destination::text as FinalDestination,
       pvi.retention_active_days as ActiveDays,
       pvi.retention_active_months as ActiveMonths,
       pvi.retention_active_years as ActiveYears,
       pvi.retention_archive_days as ArchiveDays,
       pvi.retention_archive_months as ArchiveMonths,
       pvi.retention_archive_years as ArchiveYears,
       {SuggestionColumnSql.LegacyUnproven(columns)} as IsLegacyUnproven,
       {SuggestionColumnSql.Uuid(columns, "review_operation_id")} as ReviewOperationId
from ged.document_retention_suggestion s
left join ged.classification_plan_version_item pvi
  on pvi.tenant_id = s.tenant_id
 and {SuggestionColumnSql.PlanVersionMatch(columns)}
 and pvi.id = s.suggested_rule_id
 and coalesce(pvi.reg_status,'A') = 'A'
where s.tenant_id = @tenantId and s.id = @id
""", new { tenantId, id }, cancellationToken: ct));

        if (existing is null) throw new InvalidOperationException("Sugestão não encontrada.");

        var allowed = await _authorization.CanAccessDocumentAsync(tenantId, userId, existing.DocumentId, "CLASSIFY", new Dictionary<string, string>(), ct)
                   || await _authorization.CanAccessDocumentAsync(tenantId, userId, existing.DocumentId, "RETENTION_REVIEW", new Dictionary<string, string>(), ct);
        if (!allowed)
            throw new UnauthorizedAccessException("Usuário não possui autorização para revisar temporalidade deste documento.");

        if (string.Equals(existing.Status, status, StringComparison.OrdinalIgnoreCase))
            return;

        await EnsureReviewableAsync(c, tenantId, existing.Status, existing.ReviewOperationId, "RETENTION", existing.Id, existing.DocumentId, ct);

        var docState = await c.QuerySingleOrDefaultAsync<DocumentCurrentStateRow>(new CommandDefinition("""
select id as Id, coalesce(status,'') as DocStatus, coalesce(reg_status,'A') as RegStatus,
       classification_id as CurrentClassificationId, current_version_id as CurrentVersionId,
       xmin::text::bigint as Token
from ged.document
where tenant_id = @tenantId and id = @DocumentId
""", new { tenantId, DocumentId = existing.DocumentId }, cancellationToken: ct));

        if (docState is null || docState.RegStatus != "A" || docState.DocStatus == "DELETED")
            throw new InvalidOperationException("Documento não encontrado ou inativo.");

        if (status == "REJECTED")
        {
            var updated = await c.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion
set status = 'REJECTED', reviewed_by = @userId, reviewed_at = now(), review_notes = @notes
where tenant_id = @tenantId and id = @id and status in ('PENDING','FAILED')
""", new { tenantId, id, userId, notes }, cancellationToken: ct));

            if (updated == 0) throw new InvalidOperationException("Sugestão já revisada ou não pendente.");
            await Audit(tenantId, userId, "RETENTION_SUGGESTION_REJECTED", existing.DocumentId, new { id, notes }, ct);
            return;
        }

        if (status == "ACCEPTED")
        {
            if (existing.IsLegacyUnproven || !existing.VersionId.HasValue || !SuggestionColumnSql.CanApplyReview(columns))
                throw new InvalidOperationException("Recomendação legada ou sem origem/versionamento comprovado deve ser reanalisada antes da aplicação.");
            if (!docState.CurrentVersionId.HasValue)
                throw new InvalidOperationException("Documento sem versão vigente não pode receber recomendação assistida.");
            if (existing.VersionId.Value != docState.CurrentVersionId.Value)
                throw new InvalidOperationException("A recomendação de temporalidade refere-se a uma versão anterior do documento.");
            if (!existing.RuleId.HasValue)
                throw new InvalidOperationException("Recomendação de temporalidade sem regra comprovada deve ser revisada antes da aplicação.");
            if (!existing.PlanVersionId.HasValue || !existing.SuggestedClassificationId.HasValue || !existing.HasCanonicalRetentionRule())
                throw new InvalidOperationException("Recomendação de temporalidade sem item versionado completo deve ser reanalisada antes da aplicação.");
            if (docState.CurrentClassificationId != existing.SuggestedClassificationId)
                throw new InvalidOperationException("A recomendação de temporalidade depende da classificação sugerida. Aplique a classificação e reanalise antes de aprovar a temporalidade.");

            var operationId = Guid.NewGuid();
            var applicationCompleted = false;
            var claimed = await c.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion s
set status = 'PROCESSING',
    reviewed_by = @userId,
    reviewed_at = now(),
    review_notes = @notes,
    review_operation_id = @operationId,
    processing_started_at = now(),
    processing_expires_at = now() + interval '15 minutes',
    failed_at = null,
    failure_code = null,
    public_failure_message = null
from ged.document d
where s.tenant_id = @tenantId
  and s.id = @id
  and s.status in ('PENDING','FAILED')
  and d.tenant_id = s.tenant_id
  and d.id = s.document_id
  and coalesce(d.reg_status,'A') = 'A'
  and coalesce(d.status,'') <> 'DELETED'
  and s.version_id = d.current_version_id
  and d.classification_id = @classificationId
  and d.classification_version_id = @planVersionId
""", new { tenantId, id, userId, notes, operationId, classificationId = existing.SuggestedClassificationId.Value, planVersionId = existing.PlanVersionId.Value }, cancellationToken: ct));

            if (claimed == 0) throw new InvalidOperationException("Sugestão já revisada concorrentemente.");

            try
            {
                // Recalcular temporalidade com a base canônica (BuildRetentionCte)
                var recalcResult = await _retentionJobs.RecalculateOneAsync(tenantId, existing.DocumentId, 30, ct);
                if (recalcResult <= 0)
                    throw new InvalidOperationException("Nenhum documento foi atualizado pelo recálculo de temporalidade.");
                applicationCompleted = true;
                await UpsertReviewOperationResultAsync(
                    c,
                    tenantId,
                    operationId,
                    "RETENTION",
                    id,
                    existing.DocumentId,
                    existing.VersionId,
                    null,
                    null,
                    existing.RuleId,
                    "EFFECT_CONFIRMED",
                    "EFFECT_CONFIRMED",
                    true,
                    new { recalculated = true, rows = recalcResult },
                    CancellationToken.None);

                var updated = await c.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion
set status = 'ACCEPTED',
    reviewed_by = @userId,
    reviewed_at = now(),
    review_notes = @notes,
    applied_at = now(),
    processing_expires_at = null,
    failed_at = null,
    failure_code = null,
    public_failure_message = null
where tenant_id = @tenantId and id = @id and status = 'PROCESSING' and review_operation_id = @operationId
""", new { tenantId, id, userId, notes, operationId }, cancellationToken: CancellationToken.None));

                if (updated == 0) throw new InvalidOperationException("Sugestão já revisada concorrentemente.");

                try
                {
                    await Audit(tenantId, userId, "RETENTION_SUGGESTION_ACCEPTED", existing.DocumentId, new
                    {
                        id,
                        operationId,
                        recalculated = true,
                        notes
                    }, CancellationToken.None);
                    await MarkReviewOperationAuditedAsync(c, tenantId, operationId, "RETENTION_SUGGESTION_ACCEPTED", CancellationToken.None);
                }
                catch (Exception auditEx)
                {
                    // A decisão já foi persistida; falha de auditoria deve ser tratada operacionalmente sem desfazer a decisão.
                    _logger.LogError(auditEx, "Falha de auditoria após aceite de temporalidade assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId}", tenantId, id, operationId);
                }
            }
            catch (Exception ex)
            {
                await UpsertReviewOperationResultAsync(
                    c,
                    tenantId,
                    operationId,
                    "RETENTION",
                    id,
                    existing.DocumentId,
                    existing.VersionId,
                    null,
                    null,
                    existing.RuleId,
                    applicationCompleted ? "EFFECT_CONFIRMED" : "INDETERMINATE",
                    applicationCompleted ? "FAILED_AFTER_EFFECT" : "INDETERMINATE",
                    applicationCompleted,
                    new { failure = ex.GetType().Name, message = PublicReviewFailure(ex), effectConfirmation = applicationCompleted ? "CONFIRMED" : "UNKNOWN" },
                    CancellationToken.None);
                await MarkRetentionReviewFailedAsync(c, tenantId, id, operationId, applicationCompleted, ex, CancellationToken.None);
                _logger.LogWarning(ex, "Falha na revisão de temporalidade assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId} Applied={Applied}", tenantId, id, operationId, applicationCompleted);
                throw;
            }
        }
    }

    private static string? NormalizeReviewText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeReviewStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            return null;

        return status.Trim().ToUpperInvariant();
    }

    private static void RequireReviewReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Informe a justificativa da rejeição.");
    }

    private static bool IsReviewableStatus(string? status) =>
        status is not null && (status.Equals("PENDING", StringComparison.OrdinalIgnoreCase) || status.Equals("FAILED", StringComparison.OrdinalIgnoreCase));

    private static async Task EnsureReviewableAsync(
        Npgsql.NpgsqlConnection connection,
        Guid tenantId,
        string? status,
        Guid? operationId,
        string suggestionKind,
        Guid suggestionId,
        Guid documentId,
        CancellationToken ct)
    {
        if (!IsReviewableStatus(status))
            throw new InvalidOperationException($"Sugestão já revisada anteriormente com situação '{status}'.");

        if (!status!.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
            return;

        if (!operationId.HasValue)
            throw new InvalidOperationException("Falha legada sem operação rastreável exige investigação ou revalidação explícita antes de nova decisão.");

        var effectStatus = await connection.ExecuteScalarAsync<string?>(new CommandDefinition("""
select effect_status
from ged.smart_ged_review_operation_result
where tenant_id = @tenantId
  and operation_id = @operationId
  and suggestion_kind = @suggestionKind
  and suggestion_id = @suggestionId
  and document_id = @documentId
  and coalesce(reg_status,'A') = 'A'
order by updated_at desc
limit 1
""", new { tenantId, operationId, suggestionKind, suggestionId, documentId }, cancellationToken: ct));

        if (string.Equals(effectStatus, "NO_EFFECT", StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException("A sugestão possui falha sem evidência de ausência de efeito. Resolva a pendência administrativa antes de nova decisão.");
    }

    private static string PublicReviewFailure(Exception ex)
    {
        if (ex is ArgumentException)
            return ex.Message;

        if (ex is InvalidOperationException && IsPublicReviewFailureMessage(ex.Message))
            return ex.Message;

        if (ex is OperationCanceledException)
            return "A operação foi cancelada antes da conclusão segura.";

        return "A operação não foi concluída. Revise o histórico e tente novamente se o efeito não tiver sido aplicado.";
    }

    private static bool IsPublicReviewFailureMessage(string message)
    {
        var known = new[]
        {
            "Sugestão não encontrada.",
            "Sugestão já revisada",
            "Sugestão sem classe associada",
            "Documento não encontrado ou inativo.",
            "A sugestão refere-se a uma versão documental anterior",
            "A classe sugerida não pertence",
            "A recomendação de temporalidade refere-se",
            "Nenhum documento foi atualizado pelo recálculo",
            "Sugestão legada ou sem origem",
            "Recomendação legada ou sem origem",
            "Documento sem versão vigente",
            "Falha legada sem operação",
            "A sugestão possui falha sem evidência",
            "Recomendação de temporalidade sem regra",
            "Recomendação de temporalidade sem item versionado",
            "A recomendação de temporalidade depende da classificação"
        };
        return known.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static Task UpsertReviewOperationResultAsync(
        Npgsql.NpgsqlConnection connection,
        Guid tenantId,
        Guid operationId,
        string suggestionKind,
        Guid suggestionId,
        Guid documentId,
        Guid? versionId,
        Guid? planVersionId,
        Guid? classificationId,
        Guid? retentionRuleId,
        string effectStatus,
        string resultStatus,
        bool effectApplied,
        object payload,
        CancellationToken ct)
    {
        var effectiveAt = effectApplied ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        return connection.ExecuteAsync(new CommandDefinition("""
insert into ged.smart_ged_review_operation_result(
  tenant_id, operation_id, suggestion_kind, suggestion_id, document_id, version_id, plan_version_id,
  classification_id, retention_rule_id, effect_status, result_status, effect_applied, effective_at, result_payload_json)
values(
  @tenantId, @operationId, @suggestionKind, @suggestionId, @documentId, @versionId, @planVersionId,
  @classificationId, @retentionRuleId, @effectStatus, @resultStatus, @effectApplied, @effectiveAt, cast(@payloadJson as jsonb))
on conflict (tenant_id, operation_id) where reg_status = 'A'
do update set
  effect_status = case
      when ged.smart_ged_review_operation_result.effect_status = 'EFFECT_CONFIRMED' then 'EFFECT_CONFIRMED'
      when excluded.effect_status = 'EFFECT_CONFIRMED' then 'EFFECT_CONFIRMED'
      when ged.smart_ged_review_operation_result.effect_status = 'NO_EFFECT' and excluded.effect_status = 'INDETERMINATE' then 'NO_EFFECT'
      else excluded.effect_status
  end,
  result_status = case
      when ged.smart_ged_review_operation_result.result_status = 'FINALIZED' then 'FINALIZED'
      when ged.smart_ged_review_operation_result.effect_status = 'EFFECT_CONFIRMED' and excluded.effect_status <> 'EFFECT_CONFIRMED' then ged.smart_ged_review_operation_result.result_status
      else excluded.result_status
  end,
  effect_applied = ged.smart_ged_review_operation_result.effect_applied or excluded.effect_applied,
  effective_at = coalesce(ged.smart_ged_review_operation_result.effective_at, excluded.effective_at),
  result_payload_json = coalesce(ged.smart_ged_review_operation_result.result_payload_json, '{}'::jsonb) || excluded.result_payload_json,
  updated_at = now()
""", new
        {
            tenantId,
            operationId,
            suggestionKind,
            suggestionId,
            documentId,
            versionId,
            planVersionId,
            classificationId,
            retentionRuleId,
            effectStatus,
            resultStatus,
            effectApplied,
            effectiveAt,
            payloadJson = JsonSerializer.Serialize(payload)
        }, cancellationToken: ct));
    }

    private static Task MarkReviewOperationAuditedAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, Guid operationId, string auditEvent, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
update ged.smart_ged_review_operation_result
set result_status = 'FINALIZED',
    audit_event = @auditEvent,
    audit_recorded_at = now(),
    updated_at = now()
where tenant_id = @tenantId and operation_id = @operationId and reg_status = 'A'
""", new { tenantId, operationId, auditEvent }, cancellationToken: ct));

    private static async Task MarkClassificationReviewFailedAsync(
        Npgsql.NpgsqlConnection connection,
        Guid tenantId,
        Guid id,
        Guid operationId,
        bool applicationCompleted,
        Exception ex,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion
set status = @status,
    failed_at = now(),
    failure_code = @failureCode,
    public_failure_message = @message,
    processing_expires_at = null,
    applied_at = case when @applicationCompleted then coalesce(applied_at, now()) else applied_at end
where tenant_id = @tenantId and id = @id and status = 'PROCESSING' and review_operation_id = @operationId
""", new
        {
            tenantId,
            id,
            operationId,
            status = applicationCompleted ? "FAILED_APPLIED" : "RECOVERY_INDETERMINATE",
            failureCode = ex.GetType().Name,
            message = PublicReviewFailure(ex),
            applicationCompleted
        }, cancellationToken: ct));
    }

    private static async Task MarkRetentionReviewFailedAsync(
        Npgsql.NpgsqlConnection connection,
        Guid tenantId,
        Guid id,
        Guid operationId,
        bool applicationCompleted,
        Exception ex,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion
set status = @status,
    failed_at = now(),
    failure_code = @failureCode,
    public_failure_message = @message,
    processing_expires_at = null,
    applied_at = case when @applicationCompleted then coalesce(applied_at, now()) else applied_at end
where tenant_id = @tenantId and id = @id and status = 'PROCESSING' and review_operation_id = @operationId
""", new
        {
            tenantId,
            id,
            operationId,
            status = applicationCompleted ? "FAILED_APPLIED" : "RECOVERY_INDETERMINATE",
            failureCode = ex.GetType().Name,
            message = PublicReviewFailure(ex),
            applicationCompleted
        }, cancellationToken: ct));
    }

    public async Task<SmartGedReviewRecoveryResult> RecoverExpiredAsync(Guid tenantId, Guid performedBy, CancellationToken ct)
    {
        var isAdmin = await _accessPolicy.IsAdminAsync(tenantId, performedBy, null, ct);
        if (!isAdmin)
            throw new UnauthorizedAccessException("Recuperação administrativa de revisões assistidas exige perfil FullAdmin.");

        await using var connection = await _db.OpenAsync(ct);
        var result = await RecoverExpiredReviewOperationsAsync(connection, tenantId, ct);
        await Audit(tenantId, performedBy, "SMART_GED_REVIEW_RECOVERY_EXECUTED", null, result, ct);
        return result;
    }

    public async Task ResolveAdministrativeAsync(Guid tenantId, Guid suggestionId, string suggestionKind, Guid performedBy, string reason, string evidence, CancellationToken ct)
    {
        reason = NormalizeReviewText(reason) ?? throw new ArgumentException("Informe a justificativa da resolução administrativa.");
        evidence = NormalizeReviewText(evidence) ?? throw new ArgumentException("Informe a evidência verificada para a resolução administrativa.");

        var isAdmin = await _accessPolicy.IsAdminAsync(tenantId, performedBy, null, ct);
        if (!isAdmin)
            throw new UnauthorizedAccessException("Resolução administrativa de revisões assistidas exige perfil FullAdmin.");

        var normalizedKind = NormalizeSuggestionKind(suggestionKind);
        await using var connection = await _db.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var resolved = normalizedKind == "CLASSIFICATION"
            ? await ResolveClassificationAdministrativeAsync(connection, tx, tenantId, suggestionId, performedBy, reason, evidence, ct)
            : await ResolveRetentionAdministrativeAsync(connection, tx, tenantId, suggestionId, performedBy, reason, evidence, ct);

        if (resolved is null)
            throw new InvalidOperationException("Pendência administrativa não encontrada ou já resolvida.");

        if (resolved.OperationId.HasValue)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
update ged.smart_ged_review_operation_result
set result_payload_json = coalesce(result_payload_json, '{}'::jsonb) || cast(@payload as jsonb),
    updated_at = now()
where tenant_id = @tenantId and operation_id = @operationId and reg_status = 'A'
""", new
            {
                tenantId,
                operationId = resolved.OperationId.Value,
                payload = JsonSerializer.Serialize(new { adminResolution = true, resolvedBy = performedBy, reason, evidence, previousStatus = resolved.PreviousStatus })
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);

        await RecordAdministrativeAuditAsync(connection, tenantId, suggestionId, normalizedKind, performedBy, null, resolved.DocumentId, resolved.OperationId, resolved.PreviousStatus, reason, evidence, ct);
    }

    public async Task<int> ReconcileAdministrativeAuditAsync(Guid tenantId, Guid performedBy, CancellationToken ct)
    {
        var isAdmin = await _accessPolicy.IsAdminAsync(tenantId, performedBy, null, ct);
        if (!isAdmin)
            throw new UnauthorizedAccessException("Reconciliação de auditoria administrativa exige perfil FullAdmin.");

        await using var connection = await _db.OpenAsync(ct);
        var pending = (await connection.QueryAsync<AdminAuditPendingRow>(new CommandDefinition("""
select 'CLASSIFICATION' as SuggestionKind, id as SuggestionId, document_id as DocumentId, review_operation_id as OperationId,
       coalesce(admin_resolution_previous_status,'') as PreviousStatus,
       coalesce(admin_resolution_reason,'') as Reason,
       coalesce(admin_resolution_evidence,'') as Evidence,
       admin_resolution_by as DecisionBy
from ged.document_classification_suggestion
where tenant_id = @tenantId
  and status = 'ADMIN_RESOLVED'
  and (admin_resolution_audit_status in ('PENDING','FAILED')
       or (admin_resolution_audit_status = 'RECORDING' and coalesce(admin_resolution_audit_claim_expires_at, admin_resolution_at) < now()))
  and coalesce(reg_status,'A') = 'A'
union all
select 'RETENTION' as SuggestionKind, id as SuggestionId, document_id as DocumentId, review_operation_id as OperationId,
       coalesce(admin_resolution_previous_status,'') as PreviousStatus,
       coalesce(admin_resolution_reason,'') as Reason,
       coalesce(admin_resolution_evidence,'') as Evidence,
       admin_resolution_by as DecisionBy
from ged.document_retention_suggestion
where tenant_id = @tenantId
  and status = 'ADMIN_RESOLVED'
  and (admin_resolution_audit_status in ('PENDING','FAILED')
       or (admin_resolution_audit_status = 'RECORDING' and coalesce(admin_resolution_audit_claim_expires_at, admin_resolution_at) < now()))
  and coalesce(reg_status,'A') = 'A'
order by SuggestionKind, SuggestionId
limit 100
""", new { tenantId, performedBy }, cancellationToken: ct))).ToArray();

        var reconciled = 0;
        foreach (var item in pending)
        {
            if (await RecordAdministrativeAuditAsync(connection, tenantId, item.SuggestionId, item.SuggestionKind, item.DecisionBy, performedBy, item.DocumentId, item.OperationId, item.PreviousStatus, item.Reason, item.Evidence, CancellationToken.None))
                reconciled++;
        }

        return reconciled;
    }

    private async Task<bool> RecordAdministrativeAuditAsync(
        Npgsql.NpgsqlConnection connection,
        Guid tenantId,
        Guid suggestionId,
        string normalizedKind,
        Guid? decisionBy,
        Guid? reconciledBy,
        Guid documentId,
        Guid? operationId,
        string previousStatus,
        string reason,
        string evidence,
        CancellationToken ct)
    {
        var auditEvent = $"SMART_GED_{normalizedKind}_ADMIN_RESOLVED";
        var eventIdentity = $"smart-ged-admin-resolution:{tenantId:N}:{normalizedKind.ToLowerInvariant()}:{suggestionId:N}";

        if (await AdministrativeAuditAlreadyRecordedAsync(connection, tenantId, auditEvent, documentId, eventIdentity, ct))
        {
            await MarkAdministrativeAuditRecordedAsync(connection, tenantId, suggestionId, normalizedKind, operationId, auditEvent, eventIdentity, ct);
            return true;
        }

        var claimed = await TryClaimAdministrativeAuditAsync(connection, tenantId, suggestionId, normalizedKind, auditEvent, eventIdentity, ct);
        if (!claimed)
            return false;

        try
        {
            await Audit(tenantId, decisionBy, auditEvent, documentId, new
            {
                suggestionId,
                operationId,
                previousStatus,
                reason,
                evidence,
                eventIdentity,
                decisionBy,
                reconciledBy,
                decisionAuthorMissing = !decisionBy.HasValue
            }, ct, eventIdentity);

            await MarkAdministrativeAuditRecordedAsync(connection, tenantId, suggestionId, normalizedKind, operationId, auditEvent, eventIdentity, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkAdministrativeAuditFailedAsync(connection, tenantId, suggestionId, normalizedKind, ex, CancellationToken.None);
            _logger.LogError(ex, "Falha ao auditar resolução administrativa SmartGED. Tenant={TenantId} Suggestion={SuggestionId} Kind={Kind} Operation={OperationId}", tenantId, suggestionId, normalizedKind, operationId);
            return false;
        }
    }

    private static async Task<bool> AdministrativeAuditAlreadyRecordedAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, string auditEvent, Guid documentId, string eventIdentity, CancellationToken ct)
    {
        var hasAppAudit = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.app_audit_log') is not null", cancellationToken: ct));
        if (hasAppAudit)
        {
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.app_audit_log
    where tenant_id = @tenantId
      and action in (@auditEvent, 'VIEW')
      and entity_name = 'SMART_GED'
      and entity_id = @documentId::text
      and correlation_id = @eventIdentity
      and coalesce(reg_status,'A') = 'A'
)
""", new { tenantId, auditEvent, documentId, eventIdentity }, cancellationToken: ct));
        }

        var hasLegacyAudit = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.audit_log') is not null", cancellationToken: ct));
        if (!hasLegacyAudit)
            return false;

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.audit_log
    where tenant_id = @tenantId
      and action::text in (@auditEvent, 'VIEW')
      and entity_name = 'SMART_GED'
      and entity_id = @documentId
      and data::text like '%' || @eventIdentity || '%'
)
""", new { tenantId, auditEvent, documentId, eventIdentity }, cancellationToken: ct));
    }

    private static async Task<bool> TryClaimAdministrativeAuditAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, Guid suggestionId, string normalizedKind, string auditEvent, string eventIdentity, CancellationToken ct)
    {
        var table = normalizedKind == "CLASSIFICATION" ? "ged.document_classification_suggestion" : "ged.document_retention_suggestion";
        var changed = await connection.ExecuteAsync(new CommandDefinition($"""
update {table}
set admin_resolution_audit_status = 'RECORDING',
    admin_resolution_audit_event = @auditEvent,
    admin_resolution_audit_event_identity = @eventIdentity,
    admin_resolution_audit_claimed_at = now(),
    admin_resolution_audit_claim_expires_at = now() + interval '10 minutes',
    admin_resolution_audit_error = null
where tenant_id = @tenantId
  and id = @suggestionId
  and status = 'ADMIN_RESOLVED'
  and (admin_resolution_audit_status in ('PENDING','FAILED')
       or (admin_resolution_audit_status = 'RECORDING' and coalesce(admin_resolution_audit_claim_expires_at, admin_resolution_at) < now()))
  and coalesce(reg_status,'A') = 'A'
""", new { tenantId, suggestionId, auditEvent, eventIdentity }, cancellationToken: ct));
        return changed > 0;
    }

    private static async Task MarkAdministrativeAuditRecordedAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, Guid suggestionId, string normalizedKind, Guid? operationId, string auditEvent, string eventIdentity, CancellationToken ct)
    {
        var table = normalizedKind == "CLASSIFICATION" ? "ged.document_classification_suggestion" : "ged.document_retention_suggestion";
        await connection.ExecuteAsync(new CommandDefinition($"""
update {table}
set admin_resolution_audit_status = 'RECORDED',
    admin_resolution_audit_event = @auditEvent,
    admin_resolution_audit_event_identity = @eventIdentity,
    admin_resolution_audit_recorded_at = now(),
    admin_resolution_audit_claim_expires_at = null,
    admin_resolution_audit_error = null
where tenant_id = @tenantId and id = @suggestionId and status = 'ADMIN_RESOLVED' and coalesce(reg_status,'A') = 'A'
""", new { tenantId, suggestionId, auditEvent, eventIdentity }, cancellationToken: ct));

        if (operationId.HasValue)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
update ged.smart_ged_review_operation_result
set result_status = 'FINALIZED',
    audit_event = @auditEvent,
    audit_recorded_at = now(),
    updated_at = now()
where tenant_id = @tenantId and operation_id = @operationId and reg_status = 'A'
""", new { tenantId, operationId = operationId.Value, auditEvent }, cancellationToken: ct));
        }
    }

    private static Task MarkAdministrativeAuditFailedAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, Guid suggestionId, string normalizedKind, Exception ex, CancellationToken ct)
    {
        var table = normalizedKind == "CLASSIFICATION" ? "ged.document_classification_suggestion" : "ged.document_retention_suggestion";
        return connection.ExecuteAsync(new CommandDefinition($"""
update {table}
set admin_resolution_audit_status = 'FAILED',
    admin_resolution_audit_error = @error
where tenant_id = @tenantId and id = @suggestionId and status = 'ADMIN_RESOLVED' and coalesce(reg_status,'A') = 'A'
""", new { tenantId, suggestionId, error = PublicReviewFailure(ex) }, cancellationToken: ct));
    }

    private static async Task<AdminResolutionRow?> ResolveClassificationAdministrativeAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction tx,
        Guid tenantId,
        Guid suggestionId,
        Guid performedBy,
        string reason,
        string evidence,
        CancellationToken ct)
    {
        return await connection.QuerySingleOrDefaultAsync<AdminResolutionRow>(new CommandDefinition("""
update ged.document_classification_suggestion
set admin_resolution_previous_status = status,
    status = 'ADMIN_RESOLVED',
    admin_resolution_by = @performedBy,
    admin_resolution_at = now(),
    admin_resolution_reason = @reason,
    admin_resolution_evidence = @evidence,
    admin_resolution_audit_status = 'PENDING',
    admin_resolution_audit_event = null,
    admin_resolution_audit_recorded_at = null,
    admin_resolution_audit_error = null,
    recovery_notes = concat_ws(E'\n', recovery_notes, 'Resolução administrativa: ' || @reason),
    processing_expires_at = null
where tenant_id = @tenantId
  and id = @suggestionId
  and status in ('FAILED_APPLIED','RECOVERY_INDETERMINATE')
  and coalesce(reg_status,'A') = 'A'
returning document_id as DocumentId, review_operation_id as OperationId, admin_resolution_previous_status as PreviousStatus
""", new { tenantId, suggestionId, performedBy, reason, evidence }, tx, cancellationToken: ct));
    }

    private static async Task<AdminResolutionRow?> ResolveRetentionAdministrativeAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction tx,
        Guid tenantId,
        Guid suggestionId,
        Guid performedBy,
        string reason,
        string evidence,
        CancellationToken ct)
    {
        return await connection.QuerySingleOrDefaultAsync<AdminResolutionRow>(new CommandDefinition("""
update ged.document_retention_suggestion
set admin_resolution_previous_status = status,
    status = 'ADMIN_RESOLVED',
    admin_resolution_by = @performedBy,
    admin_resolution_at = now(),
    admin_resolution_reason = @reason,
    admin_resolution_evidence = @evidence,
    admin_resolution_audit_status = 'PENDING',
    admin_resolution_audit_event = null,
    admin_resolution_audit_recorded_at = null,
    admin_resolution_audit_error = null,
    recovery_notes = concat_ws(E'\n', recovery_notes, 'Resolução administrativa: ' || @reason),
    processing_expires_at = null
where tenant_id = @tenantId
  and id = @suggestionId
  and status in ('FAILED_APPLIED','RECOVERY_INDETERMINATE')
  and coalesce(reg_status,'A') = 'A'
returning document_id as DocumentId, review_operation_id as OperationId, admin_resolution_previous_status as PreviousStatus
""", new { tenantId, suggestionId, performedBy, reason, evidence }, tx, cancellationToken: ct));
    }

    private static string NormalizeSuggestionKind(string suggestionKind)
    {
        if (suggestionKind.Equals("classification", StringComparison.OrdinalIgnoreCase) || suggestionKind.Equals("CLASSIFICATION", StringComparison.OrdinalIgnoreCase))
            return "CLASSIFICATION";
        if (suggestionKind.Equals("retention", StringComparison.OrdinalIgnoreCase) || suggestionKind.Equals("RETENTION", StringComparison.OrdinalIgnoreCase))
            return "RETENTION";
        throw new ArgumentException("Tipo de sugestão inválido para resolução administrativa.");
    }

    private static async Task<SmartGedReviewRecoveryResult> RecoverExpiredReviewOperationsAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, CancellationToken ct)
    {
        var classificationColumns = await LoadSuggestionColumns(connection, "document_classification_suggestion", ct);
        var retentionColumns = await LoadSuggestionColumns(connection, "document_retention_suggestion", ct);
        var operationTable = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(
    select 1
    from information_schema.tables
    where table_schema = 'ged' and table_name = 'smart_ged_review_operation_result')
""", cancellationToken: ct));
        if (!operationTable
            || !classificationColumns.Contains("recovered_at")
            || !retentionColumns.Contains("recovered_at")
            || !SuggestionColumnSql.CanApplyReview(classificationColumns)
            || !SuggestionColumnSql.CanApplyReview(retentionColumns))
            return new(0, 0, 0, 0, 0, 0);

        var classificationAppliedPending = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion s
set status = 'FAILED_APPLIED',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpiredWithRecordedEffect'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação expirada com efeito registrado para esta operação, mas sem decisão finalizada. Valide auditoria e não reaplique automaticamente.'),
    recovered_at = now(),
    recovery_notes = coalesce(s.recovery_notes, 'Recuperação baseada no resultado persistente da operação: efeito comprovado sem finalização da revisão.'),
    processing_expires_at = null,
    applied_at = coalesce(s.applied_at, r.effective_at)
from ged.smart_ged_review_operation_result r
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A'
  and r.tenant_id = s.tenant_id
  and r.operation_id = s.review_operation_id
  and r.suggestion_kind = 'CLASSIFICATION'
  and r.suggestion_id = s.id
  and r.document_id = s.document_id
  and coalesce(r.reg_status,'A') = 'A'
  and r.effect_status = 'EFFECT_CONFIRMED';
""", new { tenantId }, cancellationToken: ct));

        var classificationFailed = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion s
set status = 'FAILED',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpiredWithRecordedNoEffect'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação expirada com registro de ausência de efeito. Revise a causa antes de nova decisão.'),
    recovered_at = now(),
    recovery_notes = coalesce(s.recovery_notes, 'Recuperação baseada no resultado persistente da operação: ausência de efeito comprovada.'),
    processing_expires_at = null
from ged.smart_ged_review_operation_result r
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A'
  and r.tenant_id = s.tenant_id
  and r.operation_id = s.review_operation_id
  and r.suggestion_kind = 'CLASSIFICATION'
  and r.suggestion_id = s.id
  and r.document_id = s.document_id
  and coalesce(r.reg_status,'A') = 'A'
  and r.effect_status = 'NO_EFFECT';
""", new { tenantId }, cancellationToken: ct));

        var classificationIndeterminate = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion s
set status = 'RECOVERY_INDETERMINATE',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpiredIndeterminate'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação expirada sem registro transacional suficiente para comprovar aplicação ou ausência de efeito. Não reaplique automaticamente.'),
    recovered_at = now(),
    recovery_notes = coalesce(s.recovery_notes, 'Recuperação sem prova operacional: resultado indeterminado bloqueia aceite ou retry automático.'),
    processing_expires_at = null
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A';
""", new { tenantId }, cancellationToken: ct));

        var retentionAppliedPending = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion s
set status = 'FAILED_APPLIED',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpiredWithRecordedEffect'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação de temporalidade expirada com efeito registrado para esta operação, mas sem decisão finalizada. Valide a memória de cálculo antes de nova ação.'),
    recovered_at = now(),
    recovery_notes = coalesce(s.recovery_notes, 'Recuperação baseada no resultado persistente da operação: recálculo comprovado sem finalização da revisão.'),
    processing_expires_at = null,
    applied_at = coalesce(s.applied_at, r.effective_at)
from ged.smart_ged_review_operation_result r
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A'
  and r.tenant_id = s.tenant_id
  and r.operation_id = s.review_operation_id
  and r.suggestion_kind = 'RETENTION'
  and r.suggestion_id = s.id
  and r.document_id = s.document_id
  and coalesce(r.reg_status,'A') = 'A'
  and r.effect_status = 'EFFECT_CONFIRMED';
""", new { tenantId }, cancellationToken: ct));

        var retentionFailed = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion s
set status = 'FAILED',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpiredWithRecordedNoEffect'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação de temporalidade expirada com registro de ausência de recálculo. Revise a causa antes de nova decisão.'),
    recovered_at = now(),
    recovery_notes = coalesce(s.recovery_notes, 'Recuperação baseada no resultado persistente da operação: ausência de efeito comprovada.'),
    processing_expires_at = null
from ged.smart_ged_review_operation_result r
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A'
  and r.tenant_id = s.tenant_id
  and r.operation_id = s.review_operation_id
  and r.suggestion_kind = 'RETENTION'
  and r.suggestion_id = s.id
  and r.document_id = s.document_id
  and coalesce(r.reg_status,'A') = 'A'
  and r.effect_status = 'NO_EFFECT';
""", new { tenantId }, cancellationToken: ct));

        var retentionIndeterminate = await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_retention_suggestion s
set status = 'RECOVERY_INDETERMINATE',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpiredIndeterminate'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação de temporalidade expirada sem registro transacional suficiente para comprovar recálculo ou ausência de efeito. Não reaplique automaticamente.'),
    recovered_at = now(),
    recovery_notes = coalesce(s.recovery_notes, 'Recuperação sem prova operacional: resultado indeterminado bloqueia aceite ou retry automático.'),
    processing_expires_at = null
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A';
""", new { tenantId }, cancellationToken: ct));

        return new(classificationFailed, classificationAppliedPending, classificationIndeterminate, retentionFailed, retentionAppliedPending, retentionIndeterminate);
    }

    public async Task<SmartGedSearchResult> SearchAsync(SmartGedSearchQuery query, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        await using var c = await _db.OpenAsync(ct);
        var term = (query.Text ?? string.Empty).Trim();
        if (!query.UserId.HasValue)
            return new(term, [], 0, 0, Math.Max(1, query.Page), Math.Clamp(query.Limit, 1, 100));

        var hasFilters = query.FolderId.HasValue || query.ClassificationId.HasValue || query.TypeId.HasValue
            || query.CreatedAfter.HasValue || query.CreatedBefore.HasValue || !string.IsNullOrWhiteSpace(query.RetentionStatus);

        if (term.Length < 2 && !hasFilters)
            return new(term, [], 0, 0, query.Page, Math.Clamp(query.Limit, 1, 100));

        var createdAfter = query.CreatedAfter;
        var createdBefore = query.CreatedBefore;
        if (createdAfter.HasValue && createdBefore.HasValue && createdAfter.Value > createdBefore.Value)
        {
            throw new ArgumentException("A data inicial não pode ser superior à data final.");
        }

        var exclusiveEndOfDay = createdBefore.HasValue ? createdBefore.Value.Date.AddDays(1) : (DateTime?)null;
        var analysisColumns = await LoadSuggestionColumns(c, "document_ai_analysis", ct);
        var qualityAvailable = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.document_quality_issue') is not null", cancellationToken: ct));
        var qualityStatusSql = qualityAvailable
            ? "case when exists(select 1 from ged.document_quality_issue qi where qi.tenant_id=d.tenant_id and qi.document_id=d.id and qi.status='OPEN') then 'ATENÇÃO' else 'OK' end"
            : "'OK'";
        var analysisLateral = $"""
left join lateral (
    select {AnalysisColumnSql.LateralColumns(analysisColumns)}
    from ged.document_ai_analysis a
    where a.tenant_id = d.tenant_id and a.document_id = d.id and a.reg_status = 'A'
    order by {AnalysisColumnSql.Order(analysisColumns)}
    limit 1
) a on true
""";

        var sql = new System.Text.StringBuilder($"""
select d.id as DocumentId,
       coalesce(d.title, 'Documento') as Document,
       a.extracted_summary as Summary,
       a.extracted_text as ExtractedText,
       {ArchivalClassificationSql.Label()} as Classification,
       {qualityStatusSql} as QualityStatus,
       v.version_number as VersionNumber,
       d.retention_status as RetentionStatus,
       case
           when a.id is null then false
           when a.version_id is not null and d.current_version_id is not null and a.version_id <> d.current_version_id then true
           when coalesce(a.is_legacy_unproven, false) then true
           when a.created_at is not null and d.updated_at is not null and a.created_at < d.updated_at then true
           else false
       end as IsAnalysisOutdated,
       coalesce(a.is_legacy_unproven, a.version_id is null and a.id is not null) as IsLegacyUnproven,
       a.version_id as AnalyzedVersionId,
       d.current_version_id as CurrentVersionId
from ged.document d
{analysisLateral}
left join ged.document_version v on v.id = d.current_version_id
left join ged.classification_plan cp on cp.id = d.classification_id and cp.tenant_id = d.tenant_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id = d.tenant_id and pvi.version_id = d.classification_version_id and pvi.classification_id = d.classification_id
where d.tenant_id = @TenantId
  and coalesce(d.reg_status, 'A') = 'A'
  and coalesce(d.status, '') <> 'DELETED'
""");

        var dp = new DynamicParameters();
        dp.Add("TenantId", query.TenantId);

        if (!string.IsNullOrWhiteSpace(term))
        {
            sql.Append(" and (coalesce(d.title, '') ilike @pattern or coalesce(a.extracted_text, '') ilike @pattern or coalesce(a.extracted_summary, '') ilike @pattern or coalesce(pvi.code, cp.code, '') ilike @pattern or coalesce(pvi.name, cp.name, '') ilike @pattern)");
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
        if (createdAfter.HasValue)
        {
            sql.Append(" and d.created_at >= @CreatedAfter");
            dp.Add("CreatedAfter", createdAfter.Value);
        }
        if (exclusiveEndOfDay.HasValue)
        {
            sql.Append(" and d.created_at < @ExclusiveEndOfDay");
            dp.Add("ExclusiveEndOfDay", exclusiveEndOfDay.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.RetentionStatus))
        {
            sql.Append(" and d.retention_status = @RetentionStatus");
            dp.Add("RetentionStatus", query.RetentionStatus);
        }

        sql.Append(" order by d.created_at desc, d.id desc");

        var candidateIds = (await c.QueryAsync<Guid>(new CommandDefinition(
            $"select q.documentid from ({sql}) q",
            dp,
            cancellationToken: ct))).ToArray();

        var docIds = candidateIds.Distinct().ToArray();
        var authorizedIds = await _authorization.FilterDocumentsAsync(query.TenantId, query.UserId.Value, docIds, "VIEW", ct);

        var pageSize = Math.Clamp(query.Limit, 1, 100);
        var page = Math.Max(1, query.Page);
        var totalCount = candidateIds.Count(authorizedIds.Contains);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var skip = checked((page - 1) * pageSize);
        var pageIds = candidateIds
            .Where(authorizedIds.Contains)
            .Skip(skip)
            .Take(pageSize)
            .ToArray();

        IReadOnlyList<SearchRow> pagedRows = [];
        if (pageIds.Length > 0)
        {
            pagedRows = (await c.QueryAsync<SearchRow>(new CommandDefinition($"""
select d.id as DocumentId,
       coalesce(d.title, 'Documento') as Document,
       a.extracted_summary as Summary,
       a.extracted_text as ExtractedText,
       {ArchivalClassificationSql.Label()} as Classification,
       {qualityStatusSql} as QualityStatus,
       v.version_number as VersionNumber,
       d.retention_status as RetentionStatus,
       case
           when a.id is null then false
           when a.version_id is not null and d.current_version_id is not null and a.version_id <> d.current_version_id then true
           when coalesce(a.is_legacy_unproven, false) then true
           when a.created_at is not null and d.updated_at is not null and a.created_at < d.updated_at then true
           else false
       end as IsAnalysisOutdated,
       coalesce(a.is_legacy_unproven, a.version_id is null and a.id is not null) as IsLegacyUnproven,
       a.version_id as AnalyzedVersionId,
       d.current_version_id as CurrentVersionId
from ged.document d
{analysisLateral}
left join ged.document_version v on v.id = d.current_version_id
left join ged.classification_plan cp on cp.id = d.classification_id and cp.tenant_id = d.tenant_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id = d.tenant_id and pvi.version_id = d.classification_version_id and pvi.classification_id = d.classification_id
where d.tenant_id = @TenantId and d.id = any(@PageIds)
order by array_position(@PageIds, d.id)
""", new { query.TenantId, PageIds = pageIds }, cancellationToken: ct))).ToArray();
        }

        var items = pagedRows
            .Select(x => new SmartGedSearchItem(
                x.DocumentId,
                x.Document,
                x.Summary,
                x.Classification,
                null,
                x.QualityStatus,
                BuildMatchingExcerpt(x.ExtractedText, x.Summary, term),
                x.VersionNumber,
                x.RetentionStatus,
                x.IsAnalysisOutdated,
                x.IsLegacyUnproven,
                x.AnalyzedVersionId,
                x.CurrentVersionId))
            .ToArray();

        clock.Stop();

        await c.ExecuteAsync(new CommandDefinition("insert into ged.smart_search_query_log(tenant_id,user_id,query_text,normalized_query,result_count,execution_ms,payload_json) values(@TenantId,@UserId,@Text,@normalized,@count,@ms,cast(@payload as jsonb))", new { query.TenantId, query.UserId, query.Text, normalized = term.ToLowerInvariant(), count = totalCount, ms = (int)clock.ElapsedMilliseconds, payload = JsonSerializer.Serialize(new { limit = pageSize, page, query.FolderId, query.ClassificationId, query.TypeId, query.RetentionStatus }) }, cancellationToken: ct));
        await Audit(query.TenantId, query.UserId, "SMART_SEARCH_EXECUTED", null, new { query = term, resultCount = totalCount }, ct);
        return new(term, items, (int)clock.ElapsedMilliseconds, totalCount, page, pageSize);
    }

    private static string BuildMatchingExcerpt(string? fullText, string? summary, string term)
    {
        var text = string.IsNullOrWhiteSpace(fullText) ? summary ?? string.Empty : fullText;
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = Regex.Replace(text, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(term))
            return clean.Length <= 240 ? clean : clean[..240] + "…";

        var idx = clean.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return clean.Length <= 240 ? clean : clean[..240] + "…";

        var start = Math.Max(0, idx - 60);
        var length = Math.Min(clean.Length - start, 240);
        var snippet = clean.Substring(start, length);
        if (start > 0) snippet = "…" + snippet;
        if (start + length < clean.Length) snippet += "…";
        return snippet;
    }

    private static async Task<SmartGedSuggestionPage<DocumentClassificationSuggestionItem>> ListClassificationPage(Npgsql.NpgsqlConnection c, Guid tenantId, string? status, Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var offset = checked((page - 1) * pageSize);
        var whereStatus = status is null ? "" : "and s.status = @status";
        var total = await CountClassificationReviews(c, tenantId, status, userId, ct);

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        offset = checked((page - 1) * pageSize);
        var args = ReviewPageArgs(tenantId, userId, status, pageSize, offset);

        var items = await ListClassifications(c, tenantId, $"""
  {whereStatus}
  and {AbacAuthorizationService.DocumentAccessPredicate("d")}
""", args, ct, "s.created_at desc, s.confidence desc, s.id desc", "limit @pageSize offset @offset");
        return new(items, total, page, pageSize);
    }

    private static async Task<HashSet<string>> LoadSuggestionColumns(Npgsql.NpgsqlConnection c, string table, CancellationToken ct)
    {
        var names = await c.QueryAsync<string>(new CommandDefinition("""
select column_name
from information_schema.columns
where table_schema = 'ged' and table_name = @table
""", new { table }, cancellationToken: ct));
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListClassifications(Npgsql.NpgsqlConnection c, Guid tenantId, string filter, object args, CancellationToken ct, string orderBy = "s.confidence desc, s.created_at desc", string pageClause = "")
    {
        var columns = await LoadSuggestionColumns(c, "document_classification_suggestion", ct);
        var sql = $"""
select s.id as Id,
       s.document_id as DocumentId,
       s.suggested_classification_code as Code,
       s.suggested_classification_title as Title,
       s.suggested_reason as Reason,
       s.confidence as Confidence,
       s.status as Status,
       d.title as DocumentTitle,
       v.version_number as VersionNumber,
       {ArchivalClassificationSql.Label()} as CurrentClassification,
       s.created_at as CreatedAt,
       {SuggestionColumnSql.Obsolete(columns)} as IsObsolete,
       s.review_notes as ReviewNotes,
       {SuggestionColumnSql.Uuid(columns, "version_id")} as VersionId,
       {SuggestionColumnSql.Uuid(columns, "plan_version_id")} as PlanVersionId,
       {SuggestionColumnSql.LegacyUnproven(columns)} as IsLegacyUnproven,
       {SuggestionColumnSql.Uuid(columns, "review_operation_id")} as ReviewOperationId,
       {SuggestionColumnSql.Timestamp(columns, "processing_started_at")} as ProcessingStartedAt,
       {SuggestionColumnSql.Text(columns, "public_failure_message")} as PublicFailureMessage,
       {SuggestionColumnSql.Timestamp(columns, "recovered_at")} as RecoveredAt,
       {SuggestionColumnSql.Text(columns, "recovery_notes")} as RecoveryNotes,
       {SuggestionColumnSql.Uuid(columns, "admin_resolution_by")} as AdminResolutionBy,
       {SuggestionColumnSql.Timestamp(columns, "admin_resolution_at")} as AdminResolutionAt,
       {SuggestionColumnSql.Text(columns, "admin_resolution_reason")} as AdminResolutionReason,
       {SuggestionColumnSql.Text(columns, "admin_resolution_evidence")} as AdminResolutionEvidence,
       {SuggestionColumnSql.Text(columns, "admin_resolution_audit_status")} as AdminResolutionAuditStatus
from ged.document_classification_suggestion s
left join ged.document d on d.id = s.document_id and d.tenant_id = s.tenant_id
left join ged.document_version v on v.id = d.current_version_id
left join ged.classification_plan cp on cp.id = d.classification_id and cp.tenant_id = d.tenant_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id = d.tenant_id and pvi.version_id = d.classification_version_id and pvi.classification_id = d.classification_id
where s.tenant_id = @tenantId and s.reg_status = 'A' {filter}
order by {orderBy}
{pageClause}
""";
        var rows = await c.QueryAsync<ClassificationRow>(new CommandDefinition(sql, args, cancellationToken: ct));
        return rows.Select(x => new DocumentClassificationSuggestionItem(
            x.Id, x.DocumentId, x.Code, x.Title, x.Reason, x.Confidence, x.Status,
            x.DocumentTitle, x.VersionNumber, x.CurrentClassification, x.CreatedAt, x.IsObsolete, x.ReviewNotes,
            x.VersionId, x.PlanVersionId, x.IsLegacyUnproven, x.ReviewOperationId, x.ProcessingStartedAt, x.PublicFailureMessage,
            x.RecoveredAt, x.RecoveryNotes, x.AdminResolutionBy, x.AdminResolutionAt, x.AdminResolutionReason, x.AdminResolutionEvidence,
            x.AdminResolutionAuditStatus)).ToArray();
    }

    private static async Task<SmartGedSuggestionPage<DocumentRetentionSuggestionItem>> ListRetentionPage(Npgsql.NpgsqlConnection c, Guid tenantId, string? status, Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var offset = checked((page - 1) * pageSize);
        var whereStatus = status is null ? "" : "and s.status = @status";
        var total = await CountRetentionReviews(c, tenantId, status, userId, ct);

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        offset = checked((page - 1) * pageSize);
        var args = ReviewPageArgs(tenantId, userId, status, pageSize, offset);

        var items = await ListRetentions(c, tenantId, $"""
  {whereStatus}
  and {AbacAuthorizationService.DocumentAccessPredicate("d")}
""", args, ct, "s.created_at desc, s.confidence desc, s.id desc", "limit @pageSize offset @offset");
        return new(items, total, page, pageSize);
    }

    private static DynamicParameters ReviewPageArgs(Guid tenantId, Guid userId, string? status, int pageSize, int offset)
    {
        var args = new DynamicParameters();
        args.Add("tenantId", tenantId);
        args.Add("userId", userId);
        args.Add("status", status);
        args.Add("pageSize", pageSize);
        args.Add("offset", offset);
        args.Add("permissionCode", "Documents.View");
        args.Add("write", false);
        args.Add("hour", DateTime.UtcNow.Hour);
        return args;
    }

    private static Task<int> CountClassificationReviews(Npgsql.NpgsqlConnection c, Guid tenantId, string? status, Guid userId, CancellationToken ct)
    {
        var whereStatus = status is null ? "" : "and s.status = @status";
        var args = ReviewPageArgs(tenantId, userId, status, 10, 0);
        return c.ExecuteScalarAsync<int>(new CommandDefinition($"""
select count(*)
from ged.document_classification_suggestion s
join ged.document d on d.id = s.document_id and d.tenant_id = s.tenant_id
where s.tenant_id = @tenantId and s.reg_status = 'A'
  {whereStatus}
  and {AbacAuthorizationService.DocumentAccessPredicate("d")}
""", args, cancellationToken: ct));
    }

    private static Task<int> CountRetentionReviews(Npgsql.NpgsqlConnection c, Guid tenantId, string? status, Guid userId, CancellationToken ct)
    {
        var whereStatus = status is null ? "" : "and s.status = @status";
        var args = ReviewPageArgs(tenantId, userId, status, 10, 0);
        return c.ExecuteScalarAsync<int>(new CommandDefinition($"""
select count(*)
from ged.document_retention_suggestion s
join ged.document d on d.id = s.document_id and d.tenant_id = s.tenant_id
where s.tenant_id = @tenantId and s.reg_status = 'A'
  {whereStatus}
  and {AbacAuthorizationService.DocumentAccessPredicate("d")}
""", args, cancellationToken: ct));
    }

    private static async Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListRetentions(Npgsql.NpgsqlConnection c, Guid tenantId, string filter, object args, CancellationToken ct, string orderBy = "s.confidence desc, s.created_at desc", string pageClause = "")
    {
        var columns = await LoadSuggestionColumns(c, "document_retention_suggestion", ct);
        var sql = $"""
select s.id as Id,
       s.document_id as DocumentId,
       s.suggested_phase as Phase,
       s.suggested_final_destination as FinalDestination,
       s.suggested_trigger_event as TriggerEvent,
       s.suggested_retention_until as RetentionUntil,
       s.suggested_reason as Reason,
       s.confidence as Confidence,
       s.status as Status,
       d.title as DocumentTitle,
       v.version_number as VersionNumber,
       d.retention_status as CurrentRetention,
       s.created_at as CreatedAt,
       {SuggestionColumnSql.Obsolete(columns)} as IsObsolete,
       s.review_notes as ReviewNotes,
       {SuggestionColumnSql.Uuid(columns, "version_id")} as VersionId,
       {SuggestionColumnSql.Uuid(columns, "plan_version_id")} as PlanVersionId,
       {SuggestionColumnSql.LegacyUnproven(columns)} as IsLegacyUnproven,
       {SuggestionColumnSql.Uuid(columns, "review_operation_id")} as ReviewOperationId,
       {SuggestionColumnSql.Timestamp(columns, "processing_started_at")} as ProcessingStartedAt,
       {SuggestionColumnSql.Text(columns, "public_failure_message")} as PublicFailureMessage,
       {SuggestionColumnSql.Timestamp(columns, "recovered_at")} as RecoveredAt,
       {SuggestionColumnSql.Text(columns, "recovery_notes")} as RecoveryNotes,
       {SuggestionColumnSql.Uuid(columns, "admin_resolution_by")} as AdminResolutionBy,
       {SuggestionColumnSql.Timestamp(columns, "admin_resolution_at")} as AdminResolutionAt,
       {SuggestionColumnSql.Text(columns, "admin_resolution_reason")} as AdminResolutionReason,
       {SuggestionColumnSql.Text(columns, "admin_resolution_evidence")} as AdminResolutionEvidence,
       {SuggestionColumnSql.Text(columns, "admin_resolution_audit_status")} as AdminResolutionAuditStatus
from ged.document_retention_suggestion s
left join ged.document d on d.id = s.document_id and d.tenant_id = s.tenant_id
left join ged.document_version v on v.id = d.current_version_id
where s.tenant_id = @tenantId and s.reg_status = 'A' {filter}
order by {orderBy}
{pageClause}
""";
        var rows = await c.QueryAsync<RetentionRow>(new CommandDefinition(sql, args, cancellationToken: ct));
        return rows.Select(x => new DocumentRetentionSuggestionItem(
            x.Id, x.DocumentId, x.Phase, x.FinalDestination, x.TriggerEvent,
            x.RetentionUntil.HasValue ? DateOnly.FromDateTime(x.RetentionUntil.Value) : null,
            x.Reason, x.Confidence, x.Status,
            x.DocumentTitle, x.VersionNumber, x.CurrentRetention, x.CreatedAt, x.IsObsolete, x.ReviewNotes,
            x.VersionId, x.PlanVersionId, x.IsLegacyUnproven, x.ReviewOperationId, x.ProcessingStartedAt, x.PublicFailureMessage,
            x.RecoveredAt, x.RecoveryNotes, x.AdminResolutionBy, x.AdminResolutionAt, x.AdminResolutionReason, x.AdminResolutionEvidence,
            x.AdminResolutionAuditStatus)).ToArray();
    }

    private Task Audit(Guid tenantId, Guid? userId, string action, Guid? documentId, object data, CancellationToken ct, string? correlationId = null) =>
        _audit.WriteAsync(new AuditWriteCommand(
            tenantId,
            userId,
            action,
            "SMART_GED",
            documentId,
            "Ação de inteligência documental",
            null,
            null,
            correlationId,
            data), ct);

    private static class RetentionRuleShape
    {
        public static bool IsComplete(
            string? finalDestination,
            string? triggerEvent,
            int? activeDays,
            int? activeMonths,
            int? activeYears,
            int? archiveDays,
            int? archiveMonths,
            int? archiveYears)
        {
            if (string.Equals(finalDestination, "GUARDA_PERMANENTE", StringComparison.OrdinalIgnoreCase))
                return true;

            var hasAnyPeriod = activeDays.HasValue || activeMonths.HasValue || activeYears.HasValue || archiveDays.HasValue || archiveMonths.HasValue || archiveYears.HasValue;
            return hasAnyPeriod && !string.IsNullOrWhiteSpace(triggerEvent);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DeserializeDictionary(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new Dictionary<string, IReadOnlyList<string>>() : JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(json) ?? new();

    private static IReadOnlyList<string> DeserializeList(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static DocumentQualityIssueItem Map(IssueRow x) =>
        new(x.Id, x.DocumentId, x.Type, x.Severity, x.Title, x.RecommendedAction, x.Status);

    private sealed class DocumentHeaderRow { public Guid Id { get; set; } public string Title { get; set; } = ""; public Guid? CurrentVersionId { get; set; } };
    private sealed class PlanVersionInfoRow { public Guid Id { get; set; } public int VersionNo { get; set; } };
    private sealed class PlanRow
    {
        public Guid Id { get; set; }
        public Guid? ItemId { get; set; }
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? FinalDestination { get; set; }
        public string? TriggerEvent { get; set; }
        public int? ActiveDays { get; set; }
        public int? ActiveMonths { get; set; }
        public int? ActiveYears { get; set; }
        public int? ArchiveDays { get; set; }
        public int? ArchiveMonths { get; set; }
        public int? ArchiveYears { get; set; }

        public bool HasCanonicalRetentionRule() =>
            ItemId.HasValue && RetentionRuleShape.IsComplete(FinalDestination, TriggerEvent, ActiveDays, ActiveMonths, ActiveYears, ArchiveDays, ArchiveMonths, ArchiveYears);
    }
    private sealed class AnalysisRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Status { get; set; } = ""; public string? Summary { get; set; } public string? DocumentType { get; set; } public string? Subject { get; set; } public DateTime? DetectedDate { get; set; } public string? IdentifiersJson { get; set; } public string? SensitiveJson { get; set; } public decimal Confidence { get; set; } };
    private sealed class ClassificationSuggestionDetailRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid? ClassificationId { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public Guid? VersionId { get; set; } public Guid? PlanVersionId { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? ReviewOperationId { get; set; } };
    private sealed class RetentionSuggestionDetailRow
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public Guid? RuleId { get; set; }
        public decimal Confidence { get; set; }
        public string Status { get; set; } = "";
        public Guid? VersionId { get; set; }
        public Guid? PlanVersionId { get; set; }
        public Guid? SuggestedClassificationId { get; set; }
        public string? TriggerEvent { get; set; }
        public string? FinalDestination { get; set; }
        public int? ActiveDays { get; set; }
        public int? ActiveMonths { get; set; }
        public int? ActiveYears { get; set; }
        public int? ArchiveDays { get; set; }
        public int? ArchiveMonths { get; set; }
        public int? ArchiveYears { get; set; }
        public bool IsLegacyUnproven { get; set; }
        public Guid? ReviewOperationId { get; set; }

        public bool HasCanonicalRetentionRule() =>
            RuleId.HasValue && RetentionRuleShape.IsComplete(FinalDestination, TriggerEvent, ActiveDays, ActiveMonths, ActiveYears, ArchiveDays, ArchiveMonths, ArchiveYears);
    }
    private sealed class AdminResolutionRow { public Guid DocumentId { get; set; } public Guid? OperationId { get; set; } public string PreviousStatus { get; set; } = ""; };
    private sealed class AdminAuditPendingRow { public string SuggestionKind { get; set; } = ""; public Guid SuggestionId { get; set; } public Guid DocumentId { get; set; } public Guid? OperationId { get; set; } public string PreviousStatus { get; set; } = ""; public string Reason { get; set; } = ""; public string Evidence { get; set; } = ""; public Guid? DecisionBy { get; set; } };
    private sealed class DocumentCurrentStateRow { public Guid Id { get; set; } public string DocStatus { get; set; } = ""; public string RegStatus { get; set; } = ""; public Guid? CurrentClassificationId { get; set; } public Guid? CurrentVersionId { get; set; } public long Token { get; set; } };
    private sealed class ClassificationRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string? Code { get; set; } public string? Title { get; set; } public string? Reason { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public string? DocumentTitle { get; set; } public int? VersionNumber { get; set; } public string? CurrentClassification { get; set; } public DateTimeOffset? CreatedAt { get; set; } public bool IsObsolete { get; set; } public string? ReviewNotes { get; set; } public Guid? VersionId { get; set; } public Guid? PlanVersionId { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? ReviewOperationId { get; set; } public DateTimeOffset? ProcessingStartedAt { get; set; } public string? PublicFailureMessage { get; set; } public DateTimeOffset? RecoveredAt { get; set; } public string? RecoveryNotes { get; set; } public Guid? AdminResolutionBy { get; set; } public DateTimeOffset? AdminResolutionAt { get; set; } public string? AdminResolutionReason { get; set; } public string? AdminResolutionEvidence { get; set; } public string? AdminResolutionAuditStatus { get; set; } };
    private sealed class RetentionRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string? Phase { get; set; } public string? FinalDestination { get; set; } public string? TriggerEvent { get; set; } public DateTime? RetentionUntil { get; set; } public string? Reason { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public string? DocumentTitle { get; set; } public int? VersionNumber { get; set; } public string? CurrentRetention { get; set; } public DateTimeOffset? CreatedAt { get; set; } public bool IsObsolete { get; set; } public string? ReviewNotes { get; set; } public Guid? VersionId { get; set; } public Guid? PlanVersionId { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? ReviewOperationId { get; set; } public DateTimeOffset? ProcessingStartedAt { get; set; } public string? PublicFailureMessage { get; set; } public DateTimeOffset? RecoveredAt { get; set; } public string? RecoveryNotes { get; set; } public Guid? AdminResolutionBy { get; set; } public DateTimeOffset? AdminResolutionAt { get; set; } public string? AdminResolutionReason { get; set; } public string? AdminResolutionEvidence { get; set; } public string? AdminResolutionAuditStatus { get; set; } };
    private sealed class IssueRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Type { get; set; } = ""; public string Severity { get; set; } = ""; public string Title { get; set; } = ""; public string? RecommendedAction { get; set; } public string Status { get; set; } = ""; };
    private sealed class QualityIssueListRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string? DocumentTitle { get; set; } public string Type { get; set; } = ""; public string Severity { get; set; } = ""; public string Title { get; set; } = ""; public string? RecommendedAction { get; set; } public string Status { get; set; } = ""; public DateTime CreatedAt { get; set; } };
    private sealed class SearchRow { public Guid DocumentId { get; set; } public string Document { get; set; } = ""; public string? Summary { get; set; } public string? ExtractedText { get; set; } public string? Classification { get; set; } public string QualityStatus { get; set; } = ""; public int? VersionNumber { get; set; } public string? RetentionStatus { get; set; } public bool IsAnalysisOutdated { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? AnalyzedVersionId { get; set; } public Guid? CurrentVersionId { get; set; } };
}
