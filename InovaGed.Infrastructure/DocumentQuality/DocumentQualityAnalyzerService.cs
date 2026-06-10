using System.Text.Json;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.DocumentQuality;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.DocumentQuality;

public sealed class DocumentQualityAnalyzerService : IDocumentQualityAnalyzerService
{
    private const int BatchSize = 100;
    private readonly IDbConnectionFactory _db;
    private readonly IFileStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly ILogger<DocumentQualityAnalyzerService> _logger;

    public DocumentQualityAnalyzerService(IDbConnectionFactory db, IFileStorage storage, IAuditWriter audit, ILogger<DocumentQualityAnalyzerService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    public async Task<DocumentQualityRunResultDto> AnalyzeAllAsync(Guid tenantId, DocumentQualityFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        var runId = Guid.NewGuid();
        var started = DateTime.UtcNow;
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.document_quality_run(id, tenant_id, started_at_utc, status, message)
values (@RunId, @TenantId, @Started, 'RUNNING', 'Análise de qualidade documental iniciada')
""", new { RunId = runId, TenantId = tenantId, Started = started }, cancellationToken: ct));

        await SafeAuditAsync(tenantId, null, "DOCUMENT_QUALITY_RUN_STARTED", "DOCUMENT_QUALITY_RUN", runId, "Análise de qualidade documental iniciada", new { tenantId, runId }, ct);

        var result = new DocumentQualityRunResultDto { RunId = runId, TenantId = tenantId, StartedAtUtc = started, Status = "RUNNING" };
        try
        {
            var documentIds = (await conn.QueryAsync<Guid>(new CommandDefinition(BuildActiveDocumentsSql(filter), new { TenantId = tenantId, Limit = filter.MaxDocuments ?? 2000 }, cancellationToken: ct))).ToList();
            result.TotalDocuments = documentIds.Count;

            foreach (var chunk in documentIds.Chunk(BatchSize))
            {
                foreach (var documentId in chunk)
                {
                    try
                    {
                        var analyzed = await AnalyzeOneInternalAsync(tenantId, documentId, filter.AnalyzeStorage, filter.AnalyzeLgpd, ct);
                        IncrementStatus(result, analyzed.QualityStatus);
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        _logger.LogError(ex, "Falha ao analisar qualidade do documento {DocumentId}", documentId);
                    }
                }
            }

            result.Status = result.FailedCount > 0 ? "FINISHED_WITH_ERRORS" : "FINISHED";
            result.FinishedAtUtc = DateTime.UtcNow;
            result.Message = $"Análise concluída. Documentos={result.TotalDocuments}; Falhas={result.FailedCount}.";

            await conn.ExecuteAsync(new CommandDefinition("""
update ged.document_quality_run
set finished_at_utc=@FinishedAtUtc, status=@Status, total_documents=@TotalDocuments,
    excellent_count=@ExcellentCount, good_count=@GoodCount, warning_count=@WarningCount,
    critical_count=@CriticalCount, failed_count=@FailedCount, message=@Message
where id=@RunId and tenant_id=@TenantId
""", result, cancellationToken: ct));

            await SafeAuditAsync(tenantId, null, "DOCUMENT_QUALITY_RUN_FINISHED", "DOCUMENT_QUALITY_RUN", runId, result.Message, result, ct);
            return result;
        }
        catch (Exception ex)
        {
            result.Status = "FAILED";
            result.FinishedAtUtc = DateTime.UtcNow;
            result.Message = ex.Message;
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.document_quality_run
set finished_at_utc=@FinishedAtUtc, status='FAILED', total_documents=@TotalDocuments,
    excellent_count=@ExcellentCount, good_count=@GoodCount, warning_count=@WarningCount,
    critical_count=@CriticalCount, failed_count=@FailedCount, message=@Message
where id=@RunId and tenant_id=@TenantId
""", result, cancellationToken: ct));
            await SafeAuditAsync(tenantId, null, "DOCUMENT_QUALITY_RUN_FAILED", "DOCUMENT_QUALITY_RUN", runId, "Falha na análise de qualidade documental", new { tenantId, runId, error = ex.Message }, ct);
            throw;
        }
    }

    public Task<DocumentQualityResultDto> AnalyzeOneAsync(Guid tenantId, Guid documentId, CancellationToken ct)
        => AnalyzeOneInternalAsync(tenantId, documentId, analyzeStorage: true, analyzeLgpd: true, ct);

    public async Task<DocumentQualityDashboardDto> GetDashboardAsync(Guid tenantId, DocumentQualityFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var args = BuildResultWhere(filter, out var where);
        args.Add("TenantId", tenantId);
        args.Add("Offset", (filter.Page - 1) * filter.PageSize);
        args.Add("PageSize", filter.PageSize);

        var totals = await conn.QuerySingleOrDefaultAsync<DashboardTotals>(new CommandDefinition($"""
with latest as (
  select distinct on (tenant_id, document_id) *
  from ged.document_quality_result
  where tenant_id=@TenantId
  order by tenant_id, document_id, analyzed_at_utc desc
)
select coalesce(avg(quality_score),0)::float8 as "AverageScore",
       count(*)::int as "TotalDocuments",
       count(*) filter (where quality_status='Excelente')::int as "ExcellentCount",
       count(*) filter (where quality_status='Bom')::int as "GoodCount",
       count(*) filter (where quality_status='Atenção')::int as "WarningCount",
       count(*) filter (where quality_status='Crítico')::int as "CriticalCount",
       count(*) filter (where has_ocr=false)::int as "WithoutOcrCount",
       count(*) filter (where has_classification=false)::int as "WithoutClassificationCount",
       count(*) filter (where is_partial_incomplete=true)::int as "IncompleteCount",
       count(*) filter (where has_lgpd_risk=true)::int as "LgpdRiskCount",
       count(*) filter (where storage_file_exists=false)::int as "MissingStorageCount"
from latest
""", args, cancellationToken: ct));

        var items = (await conn.QueryAsync<DocumentQualityResultRecord>(new CommandDefinition($"""
with latest as (
  select distinct on (r.tenant_id, r.document_id) r.*
  from ged.document_quality_result r
  where r.tenant_id=@TenantId
  order by r.tenant_id, r.document_id, r.analyzed_at_utc desc
)
select l.*, d.title as "DocumentTitle", f.name as "FolderName", dt.name as "DocumentTypeName",
       count(*) over()::int as "TotalRows"
from latest l
left join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
{where}
order by l.quality_score asc, l.analyzed_at_utc desc
limit @PageSize offset @Offset
""", args, cancellationToken: ct))).ToList();

        return new DocumentQualityDashboardDto
        {
            Filter = filter,
            AverageScore = Math.Round(totals?.AverageScore ?? 0, 1),
            TotalDocuments = totals?.TotalDocuments ?? 0,
            ExcellentCount = totals?.ExcellentCount ?? 0,
            GoodCount = totals?.GoodCount ?? 0,
            WarningCount = totals?.WarningCount ?? 0,
            CriticalCount = totals?.CriticalCount ?? 0,
            WithoutOcrCount = totals?.WithoutOcrCount ?? 0,
            WithoutClassificationCount = totals?.WithoutClassificationCount ?? 0,
            IncompleteCount = totals?.IncompleteCount ?? 0,
            LgpdRiskCount = totals?.LgpdRiskCount ?? 0,
            MissingStorageCount = totals?.MissingStorageCount ?? 0,
            Items = items.Select(MapRecord).ToArray(),
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalItems = items.FirstOrDefault()?.TotalRows ?? 0
        };
    }

    public async Task<IReadOnlyList<DocumentQualityResultDto>> GetHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<DocumentQualityResultRecord>(new CommandDefinition("""
select r.*, d.title as "DocumentTitle", f.name as "FolderName", dt.name as "DocumentTypeName"
from ged.document_quality_result r
left join ged.document d on d.tenant_id=r.tenant_id and d.id=r.document_id
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
where r.tenant_id=@TenantId and r.document_id=@DocumentId
order by r.analyzed_at_utc desc
limit 50
""", new { TenantId = tenantId, DocumentId = documentId }, cancellationToken: ct));
        return rows.Select(MapRecord).ToArray();
    }

    private async Task<DocumentQualityResultDto> AnalyzeOneInternalAsync(Guid tenantId, Guid documentId, bool analyzeStorage, bool analyzeLgpd, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var schema = await LoadSchemaAsync(conn, ct);
        var row = await conn.QuerySingleOrDefaultAsync<DocumentQualityDocumentRow>(new CommandDefinition(BuildDocumentSql(schema), new { TenantId = tenantId, DocumentId = documentId }, cancellationToken: ct));
        if (row is null) throw new InvalidOperationException("Documento ativo não encontrado para análise de qualidade.");

        var issues = new List<string>();
        var recommendations = new List<string>();
        var score = 100;

        static void Penalize(ref int s, int points, string issue, string recommendation, List<string> issues, List<string> recommendations)
        {
            s -= points;
            issues.Add(issue);
            if (!string.IsNullOrWhiteSpace(recommendation)) recommendations.Add(recommendation);
        }

        var hasOcr = string.Equals(row.OcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(row.OcrText);
        var hasOcrError = row.OcrStatus is "ERROR" or "FAILED";
        var ocrPendingTooLong = (row.OcrStatus is "PENDING" or "QUEUED" or "PROCESSING" or "RUNNING") && row.OcrRequestedAt.HasValue && row.OcrRequestedAt.Value.ToUniversalTime() < DateTime.UtcNow.AddHours(-24);

        if (hasOcrError) Penalize(ref score, 25, "OCR com erro.", "Reprocessar OCR e verificar o arquivo de origem.", issues, recommendations);
        else if (!hasOcr) Penalize(ref score, 20, "Sem OCR.", "Executar OCR para tornar o conteúdo pesquisável.", issues, recommendations);
        if (ocrPendingTooLong) Penalize(ref score, 10, "OCR pendente há mais de 24h.", "Repriorizar ou reenfileirar OCR.", issues, recommendations);

        if (!row.HasClassification) Penalize(ref score, 15, "Sem classificação.", "Classificar o documento conforme o plano arquivístico.", issues, recommendations);
        if (!row.HasDocumentType) Penalize(ref score, 10, "Sem tipo documental.", "Informar o tipo documental correto.", issues, recommendations);
        if (row.HasMissingRequiredMetadata) Penalize(ref score, 15, "Sem metadados obrigatórios.", "Completar metadados obrigatórios do tipo documental.", issues, recommendations);
        if (row.IsPartialIncomplete) Penalize(ref score, 20, "Documento parcial incompleto.", "Receber as partes pendentes ou cancelar o fracionamento.", issues, recommendations);
        if (row.IsReadyToConsolidate && row.ReadyToConsolidateSinceUtc.HasValue && row.ReadyToConsolidateSinceUtc.Value.ToUniversalTime() < DateTime.UtcNow.AddDays(-3))
            Penalize(ref score, 10, "Documento pronto para consolidar há mais de 3 dias.", "Consolidar as partes do documento.", issues, recommendations);
        if (row.IsConsolidated && !hasOcr) Penalize(ref score, 15, "Documento consolidado sem OCR final.", "Executar OCR final no documento consolidado.", issues, recommendations);
        if (!row.HasValidFolder) Penalize(ref score, 15, "Pasta inválida ou nula.", "Mover o documento para uma pasta válida do GED.", issues, recommendations);

        bool? storageExists = null;
        if (analyzeStorage && !string.IsNullOrWhiteSpace(row.StoragePath))
        {
            try
            {
                storageExists = await _storage.ExistsAsync(row.StoragePath, ct);
                if (storageExists == false) Penalize(ref score, 40, "Arquivo físico não encontrado no storage.", "Restaurar o arquivo físico ou corrigir o caminho de storage.", issues, recommendations);
            }
            catch (Exception ex)
            {
                storageExists = null;
                _logger.LogWarning(ex, "Não foi possível validar storage do documento {DocumentId}", documentId);
            }
        }
        else if (string.IsNullOrWhiteSpace(row.StoragePath))
        {
            Penalize(ref score, 40, "Arquivo sem caminho de storage.", "Corrigir vínculo entre versão documental e storage.", issues, recommendations);
            storageExists = false;
        }

        var duplicate = await HasPossibleDuplicateAsync(conn, tenantId, row, ct);
        if (duplicate) Penalize(ref score, 10, "Duplicidade provável.", "Comparar versões e consolidar/remover duplicidades se confirmado.", issues, recommendations);

        var lgpdRisk = false;
        if (analyzeLgpd && IsSensitive(row))
        {
            if (!row.IsConfidential)
            {
                lgpdRisk = true;
                Penalize(ref score, 20, "Documento sensível sem marcação LGPD.", "Marcar como confidencial/sensível e revisar base legal de acesso.", issues, recommendations);
            }
            if (!row.HasRestrictedAcl)
            {
                lgpdRisk = true;
                Penalize(ref score, 25, "Permissão ampla demais para documento sensível.", "Corrigir permissão e restringir acesso por perfil/setor.", issues, recommendations);
            }
        }

        if (row.IsDeletedButSearchable) Penalize(ref score, 50, "Documento excluído logicamente ainda aparecendo na busca.", "Remover ou atualizar índice de busca.", issues, recommendations);

        if (row.IsPartialDocument)
        {
            if (row.PartialPartsCount > 0 && row.PartialPartsWithOcrCount > 0 && row.PartialPartsWithOcrCount < row.PartialPartsCount) issues.Add("OCR parcial nas partes.");
            if (row.PartialPartsCount > 0 && row.PartialPartsWithOcrCount == 0) issues.Add("Sem OCR nas partes.");
        }

        score = Math.Clamp(score, 0, 100);
        var status = ScoreStatus(score);
        var nextAction = DetermineNextAction(issues);
        var resultId = Guid.NewGuid();
        var analyzedAt = DateTime.UtcNow;
        var issuesJson = JsonSerializer.Serialize(issues);
        var recJson = JsonSerializer.Serialize(recommendations.Distinct().ToArray());

        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.document_quality_result(
    id, tenant_id, document_id, current_version_id, quality_score, quality_status,
    has_ocr, has_ocr_error, has_classification, has_document_type, has_required_metadata,
    is_partial_document, is_partial_incomplete, is_ready_to_consolidate, is_consolidated,
    storage_file_exists, has_possible_duplicate, has_lgpd_risk, issues_json, recommendations_json,
    next_action, analyzed_at_utc)
values (
    @Id, @TenantId, @DocumentId, @CurrentVersionId, @QualityScore, @QualityStatus,
    @HasOcr, @HasOcrError, @HasClassification, @HasDocumentType, @HasRequiredMetadata,
    @IsPartialDocument, @IsPartialIncomplete, @IsReadyToConsolidate, @IsConsolidated,
    @StorageFileExists, @HasPossibleDuplicate, @HasLgpdRisk, @IssuesJson::jsonb, @RecommendationsJson::jsonb,
    @NextAction, @AnalyzedAtUtc)
""", new
        {
            Id = resultId,
            TenantId = tenantId,
            DocumentId = documentId,
            row.CurrentVersionId,
            QualityScore = score,
            QualityStatus = status,
            HasOcr = hasOcr,
            HasOcrError = hasOcrError,
            HasClassification = row.HasClassification,
            HasDocumentType = row.HasDocumentType,
            HasRequiredMetadata = !row.HasMissingRequiredMetadata,
            row.IsPartialDocument,
            row.IsPartialIncomplete,
            row.IsReadyToConsolidate,
            row.IsConsolidated,
            StorageFileExists = storageExists,
            HasPossibleDuplicate = duplicate,
            HasLgpdRisk = lgpdRisk,
            IssuesJson = issuesJson,
            RecommendationsJson = recJson,
            NextAction = nextAction,
            AnalyzedAtUtc = analyzedAt
        }, cancellationToken: ct));

        var dto = new DocumentQualityResultDto
        {
            Id = resultId,
            TenantId = tenantId,
            DocumentId = documentId,
            CurrentVersionId = row.CurrentVersionId,
            DocumentTitle = row.DocumentTitle,
            FolderName = row.FolderName,
            DocumentTypeName = row.DocumentTypeName,
            QualityScore = score,
            QualityStatus = status,
            HasOcr = hasOcr,
            HasOcrError = hasOcrError,
            HasClassification = row.HasClassification,
            HasDocumentType = row.HasDocumentType,
            HasRequiredMetadata = !row.HasMissingRequiredMetadata,
            IsPartialDocument = row.IsPartialDocument,
            IsPartialIncomplete = row.IsPartialIncomplete,
            IsReadyToConsolidate = row.IsReadyToConsolidate,
            IsConsolidated = row.IsConsolidated,
            StorageFileExists = storageExists,
            HasPossibleDuplicate = duplicate,
            HasLgpdRisk = lgpdRisk,
            Issues = issues,
            Recommendations = recommendations.Distinct().ToArray(),
            NextAction = nextAction,
            AnalyzedAtUtc = analyzedAt
        };

        await SafeAuditAsync(tenantId, null, "DOCUMENT_QUALITY_DOCUMENT_ANALYZED", "DOCUMENT", documentId, $"Documento analisado: {score} - {status}", new { tenantId, documentId, score, status, issues }, ct);
        return dto;
    }

    private static DocumentQualityFilter NormalizeFilter(DocumentQualityFilter? filter)
    {
        filter ??= new DocumentQualityFilter();
        filter.Page = Math.Max(1, filter.Page);
        filter.PageSize = Math.Clamp(filter.PageSize <= 0 ? 50 : filter.PageSize, 1, 100);
        return filter;
    }

    private static void IncrementStatus(DocumentQualityRunResultDto result, string status)
    {
        if (status == "Excelente") result.ExcellentCount++;
        else if (status == "Bom") result.GoodCount++;
        else if (status == "Atenção") result.WarningCount++;
        else result.CriticalCount++;
    }

    private static string ScoreStatus(int score) => score >= 90 ? "Excelente" : score >= 75 ? "Bom" : score >= 50 ? "Atenção" : "Crítico";

    private static string? DetermineNextAction(IReadOnlyCollection<string> issues)
    {
        if (issues.Count == 0) return "Nenhuma ação necessária.";
        if (issues.Any(i => i.Contains("storage", StringComparison.OrdinalIgnoreCase) || i.Contains("Arquivo físico", StringComparison.OrdinalIgnoreCase))) return "Corrigir arquivo no storage.";
        if (issues.Any(i => i.Contains("OCR", StringComparison.OrdinalIgnoreCase))) return "Executar OCR.";
        if (issues.Any(i => i.Contains("classificação", StringComparison.OrdinalIgnoreCase))) return "Classificar documento.";
        if (issues.Any(i => i.Contains("metadados", StringComparison.OrdinalIgnoreCase))) return "Completar metadados.";
        if (issues.Any(i => i.Contains("LGPD", StringComparison.OrdinalIgnoreCase) || i.Contains("Permissão", StringComparison.OrdinalIgnoreCase))) return "Corrigir marcação LGPD e permissões.";
        if (issues.Any(i => i.Contains("consolidar", StringComparison.OrdinalIgnoreCase) || i.Contains("parcial", StringComparison.OrdinalIgnoreCase))) return "Tratar partes e consolidação.";
        return "Revisar pendências do documento.";
    }

    private static bool IsSensitive(DocumentQualityDocumentRow row)
    {
        var text = $"{row.DocumentTitle} {row.DocumentTypeName} {row.ClassificationLabel}".ToLowerInvariant();
        return row.IsConfidential || text.Contains("prontu") || text.Contains("saúde") || text.Contains("saude") || text.Contains("laudo") || text.Contains("exame") || text.Contains("paciente") || text.Contains("médic") || text.Contains("medic");
    }

    private async Task<bool> HasPossibleDuplicateAsync(Npgsql.NpgsqlConnection conn, Guid tenantId, DocumentQualityDocumentRow row, CancellationToken ct)
    {
        var sql = """
select exists(
    select 1
    from ged.document d
    join ged.document_version v on v.tenant_id=d.tenant_id and v.document_id=d.id and v.id=d.current_version_id
    where d.tenant_id=@TenantId and d.id<>@DocumentId and coalesce(d.reg_status,'A')='A'
      and (
        (lower(coalesce(d.title,''))=lower(coalesce(@Title,'')) and nullif(@Title,'') is not null)
        or (lower(coalesce(v.file_name,''))=lower(coalesce(@FileName,'')) and nullif(@FileName,'') is not null)
      )
    limit 1)
""";
        try
        {
            return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { TenantId = tenantId, row.DocumentId, Title = row.DocumentTitle, row.FileName, row.SizeBytes, row.Sha256 }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao avaliar duplicidade provável do documento {DocumentId}", row.DocumentId);
            return false;
        }
    }

    private Task SafeAuditAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string summary, object data, CancellationToken ct)
        => _audit.WriteAsync(tenantId, userId, action, entityName, entityId, summary, null, null, data, ct);

    private static string BuildActiveDocumentsSql(DocumentQualityFilter filter) => """
select d.id
from ged.document d
where d.tenant_id=@TenantId
  and coalesce(d.reg_status,'A')='A'
  and coalesce(d.status::text,'') <> 'ARCHIVED'
order by coalesce(d.updated_at, d.created_at) desc nulls last
limit @Limit
""";

    private static DynamicParameters BuildResultWhere(DocumentQualityFilter filter, out string where)
    {
        var args = new DynamicParameters();
        var predicates = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.Status)) { predicates.Add("l.quality_status=@Status"); args.Add("Status", filter.Status); }
        if (filter.ScoreMin.HasValue) { predicates.Add("l.quality_score>=@ScoreMin"); args.Add("ScoreMin", filter.ScoreMin); }
        if (filter.ScoreMax.HasValue) { predicates.Add("l.quality_score<=@ScoreMax"); args.Add("ScoreMax", filter.ScoreMax); }
        if (filter.WithoutOcr == true) predicates.Add("l.has_ocr=false");
        if (filter.OcrError == true) predicates.Add("l.has_ocr_error=true");
        if (filter.WithoutClassification == true) predicates.Add("l.has_classification=false");
        if (filter.WithoutDocumentType == true) predicates.Add("l.has_document_type=false");
        if (filter.WithoutMetadata == true) predicates.Add("l.has_required_metadata=false");
        if (filter.PartialDocument == true) predicates.Add("l.is_partial_document=true");
        if (filter.ReadyToConsolidate == true) predicates.Add("l.is_ready_to_consolidate=true");
        if (filter.LgpdRisk == true) predicates.Add("l.has_lgpd_risk=true");
        if (filter.MissingStorageFile == true) predicates.Add("l.storage_file_exists=false");
        if (filter.FolderId.HasValue) { predicates.Add("d.folder_id=@FolderId"); args.Add("FolderId", filter.FolderId); }
        if (filter.AnalyzedFrom.HasValue) { predicates.Add("l.analyzed_at_utc>=@AnalyzedFrom"); args.Add("AnalyzedFrom", filter.AnalyzedFrom); }
        if (filter.AnalyzedTo.HasValue) { predicates.Add("l.analyzed_at_utc<=@AnalyzedTo"); args.Add("AnalyzedTo", filter.AnalyzedTo); }
        where = predicates.Count == 0 ? string.Empty : "where " + string.Join(" and ", predicates);
        return args;
    }

    private async Task<QualitySchema> LoadSchemaAsync(Npgsql.NpgsqlConnection conn, CancellationToken ct)
        => await conn.QuerySingleAsync<QualitySchema>(new CommandDefinition("""
select
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='classification_id') as "HasDocumentClassificationId",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='type_id') as "HasDocumentTypeId",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='document_type_id') as "HasDocumentDocumentTypeId",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='is_confidential') as "HasDocumentIsConfidential",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='storage_path') as "HasStoragePath",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='sha256') as "HasSha256",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='size_bytes') as "HasSizeBytes",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='file_size_bytes') as "HasFileSizeBytes",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='is_partial_document') as "HasIsPartialDocument",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='is_document_incomplete') as "HasIsDocumentIncomplete",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='partial_status') as "HasPartialStatus",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_version' and column_name='consolidated_version_id') as "HasConsolidatedVersionId",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_search' and column_name='ocr_text') as "HasDocumentSearchOcrText",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_search' and column_name='document_id') as "HasDocumentSearchDocumentId",
 exists(select 1 from information_schema.columns where table_schema='ged' and table_name='document_search' and column_name='version_id') as "HasDocumentSearchVersionId",
 to_regclass('ged.document_partial_part') is not null as "HasPartialPartTable",
 to_regclass('ged.document_acl') is not null as "HasDocumentAcl",
 to_regclass('ged.document_metadata_value') is not null as "HasMetadataValue",
 to_regclass('ged.document_type_field') is not null as "HasTypeField"
""", cancellationToken: ct));

    private static string BuildDocumentSql(QualitySchema s)
    {
        var typeExpr = s.HasDocumentTypeId ? "d.type_id" : s.HasDocumentDocumentTypeId ? "d.document_type_id" : "NULL::uuid";
        var classExpr = s.HasDocumentClassificationId ? "d.classification_id" : "NULL::uuid";
        var storageExpr = s.HasStoragePath ? "v.storage_path" : "NULL::text";
        var shaExpr = s.HasSha256 ? "v.sha256" : "NULL::text";
        var sizeExpr = s.HasSizeBytes ? "v.size_bytes" : s.HasFileSizeBytes ? "v.file_size_bytes" : "0";
        var isPartialExpr = s.HasIsPartialDocument ? "coalesce(v.is_partial_document,false)" : "false";
        var incompleteExpr = s.HasIsDocumentIncomplete ? "coalesce(v.is_document_incomplete,false)" : "false";
        var partialStatusExpr = s.HasPartialStatus ? "upper(coalesce(v.partial_status,''))" : "''";
        var consolidatedExpr = s.HasConsolidatedVersionId ? "v.consolidated_version_id is not null" : $"{partialStatusExpr}='CONSOLIDATED'";
        var confidentialExpr = s.HasDocumentIsConfidential ? "coalesce(d.is_confidential,false)" : "false";
        var ocrJoin = s.HasDocumentSearchOcrText
            ? $"left join ged.document_search ds on ds.tenant_id=d.tenant_id and ({(s.HasDocumentSearchDocumentId ? "ds.document_id=d.id" : "false")} or {(s.HasDocumentSearchVersionId ? "ds.version_id=v.id" : "false")})"
            : "left join (select null::uuid tenant_id, null::text ocr_text where false) ds on false";
        var partialJoin = s.HasPartialPartTable && s.HasDocumentSearchOcrText
            ? """
left join lateral (
  select count(*)::int as parts_count,
         count(*) filter (where nullif(btrim(coalesce(pds.ocr_text,'')),'') is not null)::int as parts_with_ocr
  from ged.document_partial_part pp
  left join ged.document_search pds on pds.tenant_id=pp.tenant_id and pds.version_id=pp.version_id
  where pp.tenant_id=d.tenant_id and pp.partial_group_id=v.partial_group_id and coalesce(pp.reg_status,'A')='A'
) ps on true
""" : "left join lateral (select 0::int as parts_count, 0::int as parts_with_ocr) ps on true";
        var aclExpr = s.HasDocumentAcl ? "exists(select 1 from ged.document_acl a where a.tenant_id=d.tenant_id and a.document_id=d.id and coalesce(a.reg_status,'A')='A')" : "false";
        var missingMetaExpr = s.HasMetadataValue && s.HasTypeField ? $"""
exists (
  select 1 from ged.document_type_field tf
  where tf.tenant_id=d.tenant_id and tf.document_type_id={typeExpr} and coalesce(tf.is_required,false)=true and coalesce(tf.reg_status,'A')='A'
    and not exists (
      select 1 from ged.document_metadata_value mv
      where mv.tenant_id=d.tenant_id and mv.document_id=d.id and mv.field_id=tf.id and nullif(btrim(coalesce(mv.value_text, mv.value, '')),'') is not null
    )
)
""" : "false";

        return $"""
select d.id as "DocumentId", d.title as "DocumentTitle", d.current_version_id as "CurrentVersionId",
       f.name as "FolderName", dt.name as "DocumentTypeName", NULL::text as "ClassificationLabel",
       v.file_name as "FileName", coalesce({sizeExpr},0) as "SizeBytes", {shaExpr} as "Sha256", {storageExpr} as "StoragePath",
       coalesce(upper(oj.status::text), case when nullif(btrim(coalesce(ds.ocr_text,'')),'') is not null then 'COMPLETED' else 'NONE' end) as "OcrStatus",
       ds.ocr_text as "OcrText", oj.requested_at as "OcrRequestedAt",
       ({classExpr} is not null) as "HasClassification",
       ({typeExpr} is not null) as "HasDocumentType",
       ({missingMetaExpr}) as "HasMissingRequiredMetadata",
       ({isPartialExpr}) as "IsPartialDocument",
       (({incompleteExpr}) or {partialStatusExpr}='INCOMPLETE') as "IsPartialIncomplete",
       (({isPartialExpr}) and not (({incompleteExpr}) or {partialStatusExpr}='INCOMPLETE') and {partialStatusExpr} in ('COMPLETE','READY_TO_CONSOLIDATE')) as "IsReadyToConsolidate",
       case when {partialStatusExpr} in ('COMPLETE','READY_TO_CONSOLIDATE') then v.created_at else null end as "ReadyToConsolidateSinceUtc",
       ({consolidatedExpr}) as "IsConsolidated",
       (d.folder_id is not null and f.id is not null) as "HasValidFolder",
       {confidentialExpr} as "IsConfidential",
       {aclExpr} as "HasRestrictedAcl",
       coalesce(ps.parts_count,0) as "PartialPartsCount", coalesce(ps.parts_with_ocr,0) as "PartialPartsWithOcrCount",
       false as "IsDeletedButSearchable"
from ged.document d
left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id={typeExpr}
left join lateral (
  select j.* from ged.ocr_job j
  where j.tenant_id=d.tenant_id and j.document_version_id=v.id
  order by coalesce(j.finished_at, j.requested_at) desc nulls last limit 1
) oj on true
{ocrJoin}
{partialJoin}
where d.tenant_id=@TenantId and d.id=@DocumentId and coalesce(d.reg_status,'A')='A' and coalesce(d.status::text,'') <> 'ARCHIVED'
limit 1
""";
    }

    private static DocumentQualityResultDto MapRecord(DocumentQualityResultRecord r)
    {
        static IReadOnlyList<string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
            try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        return new DocumentQualityResultDto
        {
            Id = r.Id,
            TenantId = r.TenantId,
            DocumentId = r.DocumentId,
            CurrentVersionId = r.CurrentVersionId,
            DocumentTitle = r.DocumentTitle ?? "Documento",
            FolderName = r.FolderName,
            DocumentTypeName = r.DocumentTypeName,
            QualityScore = r.QualityScore,
            QualityStatus = r.QualityStatus,
            HasOcr = r.HasOcr,
            HasOcrError = r.HasOcrError,
            HasClassification = r.HasClassification,
            HasDocumentType = r.HasDocumentType,
            HasRequiredMetadata = r.HasRequiredMetadata,
            IsPartialDocument = r.IsPartialDocument,
            IsPartialIncomplete = r.IsPartialIncomplete,
            IsReadyToConsolidate = r.IsReadyToConsolidate,
            IsConsolidated = r.IsConsolidated,
            StorageFileExists = r.StorageFileExists,
            HasPossibleDuplicate = r.HasPossibleDuplicate,
            HasLgpdRisk = r.HasLgpdRisk,
            Issues = Parse(r.IssuesJson),
            Recommendations = Parse(r.RecommendationsJson),
            NextAction = r.NextAction,
            AnalyzedAtUtc = r.AnalyzedAtUtc
        };
    }

    private sealed class QualitySchema
    {
        public bool HasDocumentClassificationId { get; set; }
        public bool HasDocumentTypeId { get; set; }
        public bool HasDocumentDocumentTypeId { get; set; }
        public bool HasDocumentIsConfidential { get; set; }
        public bool HasStoragePath { get; set; }
        public bool HasSha256 { get; set; }
        public bool HasSizeBytes { get; set; }
        public bool HasFileSizeBytes { get; set; }
        public bool HasIsPartialDocument { get; set; }
        public bool HasIsDocumentIncomplete { get; set; }
        public bool HasPartialStatus { get; set; }
        public bool HasConsolidatedVersionId { get; set; }
        public bool HasDocumentSearchOcrText { get; set; }
        public bool HasDocumentSearchDocumentId { get; set; }
        public bool HasDocumentSearchVersionId { get; set; }
        public bool HasPartialPartTable { get; set; }
        public bool HasDocumentAcl { get; set; }
        public bool HasMetadataValue { get; set; }
        public bool HasTypeField { get; set; }
    }

    private sealed class DashboardTotals
    {
        public double AverageScore { get; set; }
        public int TotalDocuments { get; set; }
        public int ExcellentCount { get; set; }
        public int GoodCount { get; set; }
        public int WarningCount { get; set; }
        public int CriticalCount { get; set; }
        public int WithoutOcrCount { get; set; }
        public int WithoutClassificationCount { get; set; }
        public int IncompleteCount { get; set; }
        public int LgpdRiskCount { get; set; }
        public int MissingStorageCount { get; set; }
    }

    private sealed class DocumentQualityDocumentRow
    {
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = string.Empty;
        public Guid? CurrentVersionId { get; set; }
        public string? FolderName { get; set; }
        public string? DocumentTypeName { get; set; }
        public string? ClassificationLabel { get; set; }
        public string? FileName { get; set; }
        public long SizeBytes { get; set; }
        public string? Sha256 { get; set; }
        public string? StoragePath { get; set; }
        public string OcrStatus { get; set; } = "NONE";
        public string? OcrText { get; set; }
        public DateTime? OcrRequestedAt { get; set; }
        public bool HasClassification { get; set; }
        public bool HasDocumentType { get; set; }
        public bool HasMissingRequiredMetadata { get; set; }
        public bool IsPartialDocument { get; set; }
        public bool IsPartialIncomplete { get; set; }
        public bool IsReadyToConsolidate { get; set; }
        public DateTime? ReadyToConsolidateSinceUtc { get; set; }
        public bool IsConsolidated { get; set; }
        public bool HasValidFolder { get; set; }
        public bool IsConfidential { get; set; }
        public bool HasRestrictedAcl { get; set; }
        public int PartialPartsCount { get; set; }
        public int PartialPartsWithOcrCount { get; set; }
        public bool IsDeletedButSearchable { get; set; }
    }

    private sealed class DocumentQualityResultRecord
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid DocumentId { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public int QualityScore { get; set; }
        public string QualityStatus { get; set; } = string.Empty;
        public bool HasOcr { get; set; }
        public bool HasOcrError { get; set; }
        public bool HasClassification { get; set; }
        public bool HasDocumentType { get; set; }
        public bool HasRequiredMetadata { get; set; }
        public bool IsPartialDocument { get; set; }
        public bool IsPartialIncomplete { get; set; }
        public bool IsReadyToConsolidate { get; set; }
        public bool IsConsolidated { get; set; }
        public bool? StorageFileExists { get; set; }
        public bool HasPossibleDuplicate { get; set; }
        public bool HasLgpdRisk { get; set; }
        public string? IssuesJson { get; set; }
        public string? RecommendationsJson { get; set; }
        public string? NextAction { get; set; }
        public DateTime AnalyzedAtUtc { get; set; }
        public string? DocumentTitle { get; set; }
        public string? FolderName { get; set; }
        public string? DocumentTypeName { get; set; }
        public int TotalRows { get; set; }
    }
}
