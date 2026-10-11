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
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SmartGed;

public sealed class SmartGedService : IDocumentIntelligenceService, IDocumentClassificationSuggestionService, IDocumentRetentionSuggestionService, ISmartGedSearchService
{
    private readonly IDbConnectionFactory _db;
    private readonly IDocumentMetadataExtractor _extractor;
    private readonly IAuditWriter _audit;
    private readonly IRetentionJobRepository _retentionJobs;
    private readonly IDocumentClassificationCommands _commands;
    private readonly IAbacAuthorizationService _authorization;
    private readonly ILogger<SmartGedService> _logger;

    public SmartGedService(
        IDbConnectionFactory db,
        IDocumentMetadataExtractor extractor,
        IAuditWriter audit,
        IRetentionJobRepository retentionJobs,
        IDocumentClassificationCommands commands,
        IAbacAuthorizationService authorization,
        ILogger<SmartGedService> logger)
    {
        _db = db;
        _extractor = extractor;
        _audit = audit;
        _retentionJobs = retentionJobs;
        _commands = commands;
        _authorization = authorization;
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
where tenant_id=@tenantId and coalesce(reg_status,'A')='A'
order by version_no desc limit 1
""", new { tenantId }, cancellationToken: ct));

            List<PlanRow> plans = [];
            Guid? planVersionId = null;

            if (maxVersionRow is not null)
            {
                planVersionId = maxVersionRow.Id;
                // Versão vigente do plano de classificação
                plans = (await c.QueryAsync<PlanRow>(new CommandDefinition("""
select coalesce(i.classification_id, cp.id) as Id,
       coalesce(i.code, cp.code) as Code,
       coalesce(i.name, cp.name, i.title) as Title,
       coalesce(cp.description, '') as Description,
       coalesce(i.final_destination, cp.final_destination) as FinalDestination
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
                if (retCols.Contains("version_id"))
                {
                    await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_retention_suggestion(tenant_id,document_id,analysis_id,version_id,plan_version_id,suggested_phase,suggested_final_destination,suggested_reason,confidence,status)
values(@tenantId,@documentId,@analysisId,@versionId,@planVersionId,'CORRENTE',@destination,'Derivada da classificação sugerida; exige confirmação humana.',@confidence,'PENDING')
""", new { tenantId, documentId, analysisId, versionId, planVersionId, destination = best.Plan.FinalDestination ?? "REQUER_REVISAO", confidence = confidence - 10 }, cancellationToken: ct));
                }
                else
                {
                    await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_retention_suggestion(tenant_id,document_id,analysis_id,suggested_phase,suggested_final_destination,suggested_reason,confidence,status)
values(@tenantId,@documentId,@analysisId,'CORRENTE',@destination,'Derivada da classificação sugerida; exige confirmação humana.',@confidence,'PENDING')
""", new { tenantId, documentId, analysisId, destination = best.Plan.FinalDestination ?? "REQUER_REVISAO", confidence = confidence - 10 }, cancellationToken: ct));
                }

                await Audit(tenantId, null, "RETENTION_SUGGESTION_CREATED", documentId, new { analysisId, versionId }, ct);
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
        await RecoverExpiredReviewOperationsAsync(c, tenantId, ct);
        var filter = string.IsNullOrWhiteSpace(status) || string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase)
            ? ""
            : "and s.status = @status";
        var items = await ListClassifications(c, tenantId, filter, new { tenantId, status }, ct);
        if (items.Count == 0) return items;

        var docIds = items.Select(x => x.DocumentId).Distinct().ToArray();
        var authorized = await _authorization.FilterDocumentsAsync(tenantId, userId.Value, docIds, "VIEW", ct);
        return items.Where(x => authorized.Contains(x.DocumentId)).ToArray();
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
        await RecoverExpiredReviewOperationsAsync(c, tenantId, ct);
        var filter = string.IsNullOrWhiteSpace(status) || string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase)
            ? ""
            : "and s.status = @status";
        var items = await ListRetentions(c, tenantId, filter, new { tenantId, status }, ct);
        if (items.Count == 0) return items;

        var docIds = items.Select(x => x.DocumentId).Distinct().ToArray();
        var authorized = await _authorization.FilterDocumentsAsync(tenantId, userId.Value, docIds, "VIEW", ct);
        return items.Where(x => authorized.Contains(x.DocumentId)).ToArray();
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

        var existing = await c.QuerySingleOrDefaultAsync<ClassificationSuggestionDetailRow>(new CommandDefinition("""
select s.id as Id, s.document_id as DocumentId, s.suggested_classification_id as ClassificationId,
       s.confidence as Confidence, s.status as Status,
       s.version_id as VersionId, s.plan_version_id as PlanVersionId
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

        if (!IsReviewableStatus(existing.Status))
            throw new InvalidOperationException($"Sugestão já revisada anteriormente com situação '{existing.Status}'.");

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
       classification_id as CurrentClassificationId, current_version_id as CurrentVersionId
from ged.document
where tenant_id = @tenantId and id = @DocumentId
""", new { tenantId, DocumentId = existing.DocumentId }, cancellationToken: ct));

                if (docState is null || docState.RegStatus != "A" || docState.DocStatus == "DELETED")
                    throw new InvalidOperationException("Documento não encontrado ou inativo.");

                if (existing.VersionId.HasValue && docState.CurrentVersionId.HasValue && existing.VersionId.Value != docState.CurrentVersionId.Value)
                {
                    throw new InvalidOperationException("A sugestão refere-se a uma versão documental anterior à atual. É necessária uma nova análise.");
                }

                // 3. Validar classe e versão elegíveis
                var maxVersion = await c.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "select max(version_no) from ged.classification_plan_version where tenant_id=@tenantId and coalesce(reg_status,'A')='A'",
                    new { tenantId }, cancellationToken: ct));

                if (maxVersion.HasValue)
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
      and v.version_no = @maxVersion
)
""", new { tenantId, ClassificationId = existing.ClassificationId.Value, maxVersion = maxVersion.Value }, cancellationToken: ct));

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
                    SystemExecutionReason: "SMART_GED_CONFIRMED"
                );

                var saveResult = await _commands.SaveManualIntegratedAsync(saveCommand, ct);
                if (!saveResult.Success)
                    throw new InvalidOperationException(saveResult.Message);
                applicationCompleted = true;

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
                }
                catch (Exception auditEx)
                {
                    // A decisão já foi persistida; falha de auditoria deve ser tratada operacionalmente sem desfazer a decisão.
                    _logger.LogError(auditEx, "Falha de auditoria após aceite de classificação assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId}", tenantId, id, operationId);
                }
            }
            catch (Exception ex)
            {
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
        var existing = await c.QuerySingleOrDefaultAsync<RetentionSuggestionDetailRow>(new CommandDefinition("""
select s.id as Id, s.document_id as DocumentId, s.suggested_rule_id as RuleId,
       s.confidence as Confidence, s.status as Status, s.version_id as VersionId
from ged.document_retention_suggestion s
where s.tenant_id = @tenantId and s.id = @id
""", new { tenantId, id }, cancellationToken: ct));

        if (existing is null) throw new InvalidOperationException("Sugestão não encontrada.");

        var allowed = await _authorization.CanAccessDocumentAsync(tenantId, userId, existing.DocumentId, "CLASSIFY", new Dictionary<string, string>(), ct)
                   || await _authorization.CanAccessDocumentAsync(tenantId, userId, existing.DocumentId, "RETENTION_REVIEW", new Dictionary<string, string>(), ct);
        if (!allowed)
            throw new UnauthorizedAccessException("Usuário não possui autorização para revisar temporalidade deste documento.");

        if (string.Equals(existing.Status, status, StringComparison.OrdinalIgnoreCase))
            return;

        if (!IsReviewableStatus(existing.Status))
            throw new InvalidOperationException($"Sugestão já revisada anteriormente com situação '{existing.Status}'.");

        var docState = await c.QuerySingleOrDefaultAsync<DocumentCurrentStateRow>(new CommandDefinition("""
select id as Id, coalesce(status,'') as DocStatus, coalesce(reg_status,'A') as RegStatus,
       classification_id as CurrentClassificationId, current_version_id as CurrentVersionId
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
            if (existing.VersionId.HasValue && docState.CurrentVersionId.HasValue && existing.VersionId.Value != docState.CurrentVersionId.Value)
                throw new InvalidOperationException("A recomendação de temporalidade refere-se a uma versão anterior do documento.");

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
  and (s.version_id is null or d.current_version_id is null or s.version_id = d.current_version_id)
""", new { tenantId, id, userId, notes, operationId }, cancellationToken: ct));

            if (claimed == 0) throw new InvalidOperationException("Sugestão já revisada concorrentemente.");

            try
            {
                // Recalcular temporalidade com a base canônica (BuildRetentionCte)
                var recalcResult = await _retentionJobs.RecalculateOneAsync(tenantId, existing.DocumentId, 30, ct);
                if (recalcResult <= 0)
                    throw new InvalidOperationException("Nenhum documento foi atualizado pelo recálculo de temporalidade.");
                applicationCompleted = true;

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
                }
                catch (Exception auditEx)
                {
                    // A decisão já foi persistida; falha de auditoria deve ser tratada operacionalmente sem desfazer a decisão.
                    _logger.LogError(auditEx, "Falha de auditoria após aceite de temporalidade assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId}", tenantId, id, operationId);
                }
            }
            catch (Exception ex)
            {
                await MarkRetentionReviewFailedAsync(c, tenantId, id, operationId, applicationCompleted, ex, CancellationToken.None);
                _logger.LogWarning(ex, "Falha na revisão de temporalidade assistida. Tenant={TenantId} Suggestion={SuggestionId} Operation={OperationId} Applied={Applied}", tenantId, id, operationId, applicationCompleted);
                throw;
            }
        }
    }

    private static string? NormalizeReviewText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void RequireReviewReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Informe a justificativa da rejeição.");
    }

    private static bool IsReviewableStatus(string? status) =>
        status is not null && (status.Equals("PENDING", StringComparison.OrdinalIgnoreCase) || status.Equals("FAILED", StringComparison.OrdinalIgnoreCase));

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
            "Nenhum documento foi atualizado pelo recálculo"
        };
        return known.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

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
            status = applicationCompleted ? "FAILED_APPLIED" : "FAILED",
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
            status = applicationCompleted ? "FAILED_APPLIED" : "FAILED",
            failureCode = ex.GetType().Name,
            message = PublicReviewFailure(ex),
            applicationCompleted
        }, cancellationToken: ct));
    }

    private static async Task RecoverExpiredReviewOperationsAsync(Npgsql.NpgsqlConnection connection, Guid tenantId, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
update ged.document_classification_suggestion s
set status = case when d.classification_id = s.suggested_classification_id then 'ACCEPTED' else 'FAILED' end,
    applied_at = case when d.classification_id = s.suggested_classification_id then coalesce(s.applied_at, now()) else s.applied_at end,
    failed_at = case when d.classification_id = s.suggested_classification_id then s.failed_at else coalesce(s.failed_at, now()) end,
    failure_code = case when d.classification_id = s.suggested_classification_id then s.failure_code else coalesce(s.failure_code, 'ProcessingExpired') end,
    public_failure_message = case when d.classification_id = s.suggested_classification_id
        then coalesce(s.public_failure_message, 'Aplicação confirmada por recuperação automática.')
        else coalesce(s.public_failure_message, 'Operação expirada antes de confirmar aplicação. Revise e execute novamente.')
    end,
    processing_expires_at = null
from ged.document d
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and d.tenant_id = s.tenant_id
  and d.id = s.document_id
  and coalesce(s.reg_status,'A') = 'A';

update ged.document_retention_suggestion s
set status = 'FAILED',
    failed_at = coalesce(s.failed_at, now()),
    failure_code = coalesce(s.failure_code, 'ProcessingExpired'),
    public_failure_message = coalesce(s.public_failure_message, 'Operação de temporalidade expirada antes de confirmar recálculo. Revise a memória de cálculo e execute novamente.'),
    processing_expires_at = null
where s.tenant_id = @tenantId
  and s.status = 'PROCESSING'
  and s.processing_expires_at is not null
  and s.processing_expires_at < now()
  and coalesce(s.reg_status,'A') = 'A';
""", new { tenantId }, cancellationToken: ct));
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

        var sql = new System.Text.StringBuilder("""
select d.id as DocumentId,
       coalesce(d.title, 'Documento') as Document,
       a.extracted_summary as Summary,
       a.extracted_text as ExtractedText,
       coalesce(pvi.code || ' - ' || coalesce(pvi.name, pvi.title), cp.code || ' - ' || cp.name, 'Não classificado') as Classification,
       case when exists(select 1 from ged.document_quality_issue qi where qi.tenant_id=d.tenant_id and qi.document_id=d.id and qi.status='OPEN') then 'ATENÇÃO' else 'OK' end as QualityStatus,
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
left join lateral (
    select id, analysis_status, extracted_summary, extracted_text, confidence, created_at,
           version_id, is_legacy_unproven
    from ged.document_ai_analysis a
    where a.tenant_id = d.tenant_id and a.document_id = d.id and a.reg_status = 'A'
    order by (a.version_id = d.current_version_id) desc nulls last, a.created_at desc
    limit 1
) a on true
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
            sql.Append(" and (coalesce(d.title, '') ilike @pattern or coalesce(a.extracted_text, '') ilike @pattern or coalesce(a.extracted_summary, '') ilike @pattern or coalesce(pvi.code, cp.code, '') ilike @pattern or coalesce(pvi.name, pvi.title, cp.name, '') ilike @pattern)");
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
            pagedRows = (await c.QueryAsync<SearchRow>(new CommandDefinition("""
select d.id as DocumentId,
       coalesce(d.title, 'Documento') as Document,
       a.extracted_summary as Summary,
       a.extracted_text as ExtractedText,
       coalesce(pvi.code || ' - ' || coalesce(pvi.name, pvi.title), cp.code || ' - ' || cp.name, 'Não classificado') as Classification,
       case when exists(select 1 from ged.document_quality_issue qi where qi.tenant_id=d.tenant_id and qi.document_id=d.id and qi.status='OPEN') then 'ATENÇÃO' else 'OK' end as QualityStatus,
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
left join lateral (
    select id, analysis_status, extracted_summary, extracted_text, confidence, created_at,
           version_id, is_legacy_unproven
    from ged.document_ai_analysis a
    where a.tenant_id = d.tenant_id and a.document_id = d.id and a.reg_status = 'A'
    order by (a.version_id = d.current_version_id) desc nulls last, a.created_at desc
    limit 1
) a on true
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

    private static async Task<IReadOnlyList<DocumentClassificationSuggestionItem>> ListClassifications(Npgsql.NpgsqlConnection c, Guid tenantId, string filter, object args, CancellationToken ct)
    {
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
       coalesce(pvi.code || ' - ' || coalesce(pvi.name, pvi.title), cp.code || ' - ' || cp.name, 'Não classificado') as CurrentClassification,
       s.created_at as CreatedAt,
       case
           when s.version_id is not null and d.current_version_id is not null and s.version_id <> d.current_version_id then true
           when s.version_id is null and s.created_at < d.updated_at then true
           else false
       end as IsObsolete,
       s.review_notes as ReviewNotes,
       s.version_id as VersionId,
       s.plan_version_id as PlanVersionId,
       coalesce(s.is_legacy_unproven, s.version_id is null) as IsLegacyUnproven,
       s.review_operation_id as ReviewOperationId,
       s.processing_started_at as ProcessingStartedAt,
       s.public_failure_message as PublicFailureMessage
from ged.document_classification_suggestion s
left join ged.document d on d.id = s.document_id and d.tenant_id = s.tenant_id
left join ged.document_version v on v.id = d.current_version_id
left join ged.classification_plan cp on cp.id = d.classification_id and cp.tenant_id = d.tenant_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id = d.tenant_id and pvi.version_id = d.classification_version_id and pvi.classification_id = d.classification_id
where s.tenant_id = @tenantId and s.reg_status = 'A' {filter}
order by s.confidence desc, s.created_at desc
""";
        var rows = await c.QueryAsync<ClassificationRow>(new CommandDefinition(sql, args, cancellationToken: ct));
        return rows.Select(x => new DocumentClassificationSuggestionItem(
            x.Id, x.DocumentId, x.Code, x.Title, x.Reason, x.Confidence, x.Status,
            x.DocumentTitle, x.VersionNumber, x.CurrentClassification, x.CreatedAt, x.IsObsolete, x.ReviewNotes,
            x.VersionId, x.PlanVersionId, x.IsLegacyUnproven, x.ReviewOperationId, x.ProcessingStartedAt, x.PublicFailureMessage)).ToArray();
    }

    private static async Task<IReadOnlyList<DocumentRetentionSuggestionItem>> ListRetentions(Npgsql.NpgsqlConnection c, Guid tenantId, string filter, object args, CancellationToken ct)
    {
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
       case
           when s.version_id is not null and d.current_version_id is not null and s.version_id <> d.current_version_id then true
           when s.version_id is null and s.created_at < d.updated_at then true
           else false
       end as IsObsolete,
       s.review_notes as ReviewNotes,
       s.version_id as VersionId,
       s.plan_version_id as PlanVersionId,
       coalesce(s.is_legacy_unproven, s.version_id is null) as IsLegacyUnproven,
       s.review_operation_id as ReviewOperationId,
       s.processing_started_at as ProcessingStartedAt,
       s.public_failure_message as PublicFailureMessage
from ged.document_retention_suggestion s
left join ged.document d on d.id = s.document_id and d.tenant_id = s.tenant_id
left join ged.document_version v on v.id = d.current_version_id
where s.tenant_id = @tenantId and s.reg_status = 'A' {filter}
order by s.confidence desc, s.created_at desc
""";
        var rows = await c.QueryAsync<RetentionRow>(new CommandDefinition(sql, args, cancellationToken: ct));
        return rows.Select(x => new DocumentRetentionSuggestionItem(
            x.Id, x.DocumentId, x.Phase, x.FinalDestination, x.TriggerEvent,
            x.RetentionUntil.HasValue ? DateOnly.FromDateTime(x.RetentionUntil.Value) : null,
            x.Reason, x.Confidence, x.Status,
            x.DocumentTitle, x.VersionNumber, x.CurrentRetention, x.CreatedAt, x.IsObsolete, x.ReviewNotes,
            x.VersionId, x.PlanVersionId, x.IsLegacyUnproven, x.ReviewOperationId, x.ProcessingStartedAt, x.PublicFailureMessage)).ToArray();
    }

    private Task Audit(Guid tenantId, Guid? userId, string action, Guid? documentId, object data, CancellationToken ct) =>
        _audit.WriteAsync(tenantId, userId, action, "SMART_GED", documentId, "Ação de inteligência documental", null, null, data, ct);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DeserializeDictionary(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new Dictionary<string, IReadOnlyList<string>>() : JsonSerializer.Deserialize<Dictionary<string, IReadOnlyList<string>>>(json) ?? new();

    private static IReadOnlyList<string> DeserializeList(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static DocumentQualityIssueItem Map(IssueRow x) =>
        new(x.Id, x.DocumentId, x.Type, x.Severity, x.Title, x.RecommendedAction, x.Status);

    private sealed class DocumentHeaderRow { public Guid Id { get; set; } public string Title { get; set; } = ""; public Guid? CurrentVersionId { get; set; } }
    private sealed class PlanVersionInfoRow { public Guid Id { get; set; } public int VersionNo { get; set; } }
    private sealed class PlanRow { public Guid Id { get; set; } public string? Code { get; set; } public string? Title { get; set; } public string? Description { get; set; } public string? FinalDestination { get; set; } }
    private sealed class AnalysisRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Status { get; set; } = ""; public string? Summary { get; set; } public string? DocumentType { get; set; } public string? Subject { get; set; } public DateTime? DetectedDate { get; set; } public string? IdentifiersJson { get; set; } public string? SensitiveJson { get; set; } public decimal Confidence { get; set; } }
    private sealed class ClassificationSuggestionDetailRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid? ClassificationId { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public Guid? VersionId { get; set; } public Guid? PlanVersionId { get; set; } }
    private sealed class RetentionSuggestionDetailRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid? RuleId { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public Guid? VersionId { get; set; } }
    private sealed class DocumentCurrentStateRow { public Guid Id { get; set; } public string DocStatus { get; set; } = ""; public string RegStatus { get; set; } = ""; public Guid? CurrentClassificationId { get; set; } public Guid? CurrentVersionId { get; set; } }
    private sealed class ClassificationRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string? Code { get; set; } public string? Title { get; set; } public string? Reason { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public string? DocumentTitle { get; set; } public int? VersionNumber { get; set; } public string? CurrentClassification { get; set; } public DateTimeOffset? CreatedAt { get; set; } public bool IsObsolete { get; set; } public string? ReviewNotes { get; set; } public Guid? VersionId { get; set; } public Guid? PlanVersionId { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? ReviewOperationId { get; set; } public DateTimeOffset? ProcessingStartedAt { get; set; } public string? PublicFailureMessage { get; set; } }
    private sealed class RetentionRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string? Phase { get; set; } public string? FinalDestination { get; set; } public string? TriggerEvent { get; set; } public DateTime? RetentionUntil { get; set; } public string? Reason { get; set; } public decimal Confidence { get; set; } public string Status { get; set; } = ""; public string? DocumentTitle { get; set; } public int? VersionNumber { get; set; } public string? CurrentRetention { get; set; } public DateTimeOffset? CreatedAt { get; set; } public bool IsObsolete { get; set; } public string? ReviewNotes { get; set; } public Guid? VersionId { get; set; } public Guid? PlanVersionId { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? ReviewOperationId { get; set; } public DateTimeOffset? ProcessingStartedAt { get; set; } public string? PublicFailureMessage { get; set; } }
    private sealed class IssueRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string Type { get; set; } = ""; public string Severity { get; set; } = ""; public string Title { get; set; } = ""; public string? RecommendedAction { get; set; } public string Status { get; set; } = ""; }
    private sealed class SearchRow { public Guid DocumentId { get; set; } public string Document { get; set; } = ""; public string? Summary { get; set; } public string? ExtractedText { get; set; } public string? Classification { get; set; } public string QualityStatus { get; set; } = ""; public int? VersionNumber { get; set; } public string? RetentionStatus { get; set; } public bool IsAnalysisOutdated { get; set; } public bool IsLegacyUnproven { get; set; } public Guid? AnalyzedVersionId { get; set; } public Guid? CurrentVersionId { get; set; } }
}
