using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Web.Models.Ged;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
[Route("Ged/Uploads")]
public sealed class GedUploadsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IDbConnectionFactory _db;
    private readonly IUploadBatchService _batches;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly IAuditWriter _audit;
    private readonly IGedBulkDocumentActionService _bulkActions;
    private readonly IUploadBatchConsistencyService _consistency;
    private readonly ILogger<GedUploadsController> _logger;

    public GedUploadsController(ICurrentUser currentUser, IDbConnectionFactory db, IUploadBatchService batches, IGedAccessPolicyService accessPolicy, IAuditWriter audit, IGedBulkDocumentActionService bulkActions, IUploadBatchConsistencyService consistency, ILogger<GedUploadsController> logger)
    {
        _currentUser = currentUser;
        _db = db;
        _batches = batches;
        _accessPolicy = accessPolicy;
        _audit = audit;
        _bulkActions = bulkActions;
        _consistency = consistency;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var canViewTenant = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
        await using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<GedUploadBatchRowVM>(new CommandDefinition("""
SELECT b.id, b.created_by AS CreatedBy, coalesce(b.created_by_name, b.created_by::text, 'Usuário') AS UserName, b.folder_id AS FolderId, f.name AS FolderName,
       b.created_at AS CreatedAt, b.finished_at AS FinishedAt, b.status, b.total_files AS TotalFiles,
       b.success_files AS SuccessFiles, b.failed_files AS FailedFiles, b.skipped_files AS SkippedFiles,
       count(i.id) FILTER (WHERE i.status='DUPLICATE')::int AS DuplicateFiles,
       count(i.id) FILTER (WHERE i.status='ABORTED')::int AS AbortedFiles,
       CASE WHEN b.finished_at IS NULL THEN NULL ELSE (extract(epoch from (b.finished_at - b.created_at))*1000)::bigint END AS DurationMs,
       coalesce(bool_or(coalesce(i.can_retry,false) AND i.status IN ('ERROR','ABORTED','RETRYABLE')), false) AS HasRetryableItems,
       b.correlation_id AS CorrelationId
FROM ged.upload_batch b
LEFT JOIN ged.upload_batch_item i ON i.tenant_id=b.tenant_id AND i.batch_id=b.id AND coalesce(i.reg_status,'A')='A'
LEFT JOIN ged.folder f ON f.tenant_id=b.tenant_id AND f.id=b.folder_id
WHERE b.tenant_id=@tenantId AND coalesce(b.reg_status,'A')='A' AND (@canViewTenant OR b.created_by=@userId)
GROUP BY b.id, b.created_by, b.created_by_name, b.folder_id, f.name, b.created_at, b.finished_at, b.status, b.total_files, b.success_files, b.failed_files, b.skipped_files, b.correlation_id
ORDER BY b.created_at DESC
LIMIT 200;
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, canViewTenant }, cancellationToken: ct))).AsList();
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_VIEW", "UPLOAD_BATCH", null, "Histórico de uploads visualizado", null, null, new { canViewTenant }, ct);
        return View(new GedUploadHistoryVM { Batches = rows, CanViewTenantBatches = canViewTenant });
    }

    [HttpGet("{batchId:guid}")]
    public async Task<IActionResult> Details(Guid batchId, CancellationToken ct)
    {
        var canViewTenant = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
        await using var conn = await _db.OpenAsync(ct);
        try
        {
            var batch = await conn.QuerySingleOrDefaultAsync<GedUploadBatchRowVM>(new CommandDefinition("""
SELECT b.id, b.created_by AS CreatedBy, coalesce(b.created_by_name, b.created_by::text, 'Usuário') AS UserName, b.folder_id AS FolderId, f.name AS FolderName,
       b.created_at AS CreatedAt, b.finished_at AS FinishedAt, b.status, b.total_files AS TotalFiles, b.success_files AS SuccessFiles, b.failed_files AS FailedFiles, b.skipped_files AS SkippedFiles,
       b.correlation_id AS CorrelationId
FROM ged.upload_batch b
LEFT JOIN ged.folder f ON f.tenant_id=b.tenant_id AND f.id=b.folder_id
WHERE b.tenant_id=@tenantId AND b.id=@batchId AND coalesce(b.reg_status,'A')='A' AND (@canViewTenant OR b.created_by=@userId);
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, batchId, canViewTenant }, cancellationToken: ct));
            if (batch is null) return NotFound();
            var items = await LoadItemsAsync(conn, batchId, ct);
            await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_DETAIL_VIEW", "UPLOAD_BATCH", batchId, "Detalhe de upload visualizado", null, null, null, ct);
            return View(new GedUploadBatchDetailVM { Batch = batch, Items = items });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            _logger.LogError(ex, "Erro de schema ao carregar detalhe do lote de upload. BatchId={BatchId} CorrelationId={CorrelationId}", batchId, HttpContext.TraceIdentifier);
            TempData["Error"] = "A tela de uploads encontrou uma diferença de schema. Execute as migrations do sistema.";
            return RedirectToAction("Index", "SchemaHealth");
        }
    }

    [HttpGet("{batchId:guid}/Items")]
    public async Task<IActionResult> Items(Guid batchId, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        return Json(new { success = true, items = await LoadItemsAsync(conn, batchId, ct) });
    }

    [HttpPost("{batchId:guid}/RetryFailed")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryFailed(Guid batchId, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        var result = await _batches.RetryFailedAsync(_currentUser.TenantId, _currentUser.UserId, batchId, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_RETRY_FAILED", "UPLOAD_BATCH", batchId, "Retentativa de falhas solicitada", null, null, null, ct);
        return Json(new { success = result.Success, data = result.Value, message = result.Success ? "Falhas liberadas para reenvio. Se o navegador não mantiver os arquivos em memória, selecione novamente os arquivos que falharam." : result.Error?.Message });
    }

    [HttpPost("{batchId:guid}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid batchId, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("UPDATE ged.upload_batch SET status='CANCELLED', finished_at=coalesce(finished_at, now()), updated_at=now() WHERE tenant_id=@tenantId AND id=@batchId;", new { tenantId = _currentUser.TenantId, batchId }, cancellationToken: ct));
        return Json(new { success = true, message = "Lote cancelado." });
    }

    [HttpGet("LastProblem")]
    public async Task<IActionResult> LastProblem(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<LastProblemRow>(new CommandDefinition("""
SELECT b.id AS BatchId, b.folder_id AS FolderId, f.name AS FolderName, b.total_files AS Total,
       b.success_files AS SuccessCount, b.failed_files AS FailedCount, b.skipped_files AS SkippedCount,
       b.created_at AS CreatedAt
FROM ged.upload_batch b
LEFT JOIN ged.folder f ON f.tenant_id=b.tenant_id AND f.id=b.folder_id
WHERE b.tenant_id=@tenantId
  AND b.created_by=@userId
  AND coalesce(b.reg_status,'A')='A'
  AND (coalesce(b.problem_seen,false)=false OR b.created_at > now() - interval '7 days')
  AND (b.failed_files > 0 OR b.status IN ('ERROR','PARTIAL_ERROR') OR EXISTS (
      SELECT 1 FROM ged.upload_batch_item i
      WHERE i.tenant_id=b.tenant_id AND i.batch_id=b.id AND coalesce(i.reg_status,'A')='A' AND i.status IN ('ERROR','ABORTED','RETRYABLE')
  ))
ORDER BY b.created_at DESC LIMIT 1;
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId }, cancellationToken: ct));
        var hasProblem = row is not null && row.BatchId != Guid.Empty;
        return Json(hasProblem
            ? new { success = true, hasProblem = true, batchId = row!.BatchId, folderId = row.FolderId, folderName = row.FolderName, total = row.Total, successCount = row.SuccessCount, failedCount = row.FailedCount, skippedCount = row.SkippedCount, createdAt = row.CreatedAt, message = $"Seu último upload teve {row.FailedCount} falha(s)." }
            : new { success = true, hasProblem = false });
    }

    [HttpPost("{batchId:guid}/Acknowledge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(Guid batchId, [FromBody] AcknowledgeRequest? request, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("UPDATE ged.upload_batch SET problem_seen=true, acknowledged_at=now(), acknowledged_by=@userId, user_notes=COALESCE(@notes,user_notes), updated_at=now() WHERE tenant_id=@tenantId AND id=@batchId;", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, batchId, notes = request?.Notes }, cancellationToken: ct));
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_ACKNOWLEDGED", "UPLOAD_BATCH", batchId, "Lote marcado como visto", null, null, new { request?.Notes }, ct);
        return Json(new { success = true, message = "Lote marcado como visto." });
    }

    [HttpPost("{batchId:guid}/DeleteCreatedDocuments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCreatedDocuments(Guid batchId, [FromBody] BatchDocumentActionRequest? request, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        var ids = await LoadBatchDocumentIdsAsync(batchId, request?.OnlySelectedDocumentIds, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_DELETE_CREATED_START", "UPLOAD_BATCH", batchId, "Exclusão de documentos do lote iniciada", null, null, new { ids.Count, request?.Reason }, ct);
        var result = await _bulkActions.DeleteAsync(_currentUser.TenantId, _currentUser.UserId, User, new BulkDocumentActionRequest { DocumentIds = ids, Reason = request?.Reason ?? "Reinício do upload após falha parcial" }, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_DELETE_CREATED_FINISH", "UPLOAD_BATCH", batchId, "Exclusão de documentos do lote finalizada", null, null, result, ct);
        return Json(new { result.Success, result.Requested, result.Succeeded, result.Failed, result.Message, result.Items });
    }

    [HttpPost("{batchId:guid}/MarkCreatedIncomplete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCreatedIncomplete(Guid batchId, [FromBody] BatchDocumentActionRequest? request, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        var ids = await LoadBatchDocumentIdsAsync(batchId, request?.OnlySelectedDocumentIds, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_MARK_CREATED_INCOMPLETE_START", "UPLOAD_BATCH", batchId, "Marcação de incompletos do lote iniciada", null, null, new { ids.Count, request?.Reason }, ct);
        var result = await _bulkActions.MarkIncompleteAsync(_currentUser.TenantId, _currentUser.UserId, User, new BulkDocumentActionRequest { DocumentIds = ids, Reason = request?.Reason ?? "Upload precisa de conferência/complementação" }, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_MARK_CREATED_INCOMPLETE_FINISH", "UPLOAD_BATCH", batchId, "Marcação de incompletos do lote finalizada", null, null, result, ct);
        return Json(new { result.Success, result.Requested, result.Succeeded, result.Failed, result.Message, result.Items });
    }

    [HttpPost("{batchId:guid}/Recalculate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recalculate(Guid batchId, CancellationToken ct)
    {
        if (!await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct)) return Forbid();
        var result = await _consistency.RecalculateAsync(_currentUser.TenantId, batchId, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_RECALCULATE", "UPLOAD_BATCH", batchId, "Consistência do lote recalculada", null, null, result.Value, ct);
        return Json(new { success = result.Success, data = result.Value, message = result.Success ? "Lote recalculado." : result.Error?.Message });
    }

    [HttpGet("{batchId:guid}/ExportCsv")]
    public async Task<IActionResult> ExportCsv(Guid batchId, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        var items = await LoadItemsAsync(conn, batchId, ct);
        var sb = new StringBuilder();
        sb.AppendLine("arquivo_original;status;mensagem_erro;etapa_erro;document_id;version_id;tamanho_bytes;duracao_ms;correlation_id");
        foreach (var i in items) sb.AppendLine(string.Join(';', Csv(i.OriginalFileName), Csv(i.Status), Csv(i.ErrorMessage), Csv(i.ErrorStep), Csv(i.DocumentId?.ToString()), Csv(i.VersionId?.ToString()), i.SizeBytes?.ToString() ?? "", i.ElapsedMs?.ToString() ?? "", Csv(i.CorrelationId)));
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_EXPORT_CSV", "UPLOAD_BATCH", batchId, "CSV textual de upload exportado", null, null, new { items.Count }, ct);
        return Content(sb.ToString(), "text/csv", Encoding.UTF8);
    }

    private sealed class LastProblemRow { public Guid BatchId { get; set; } public Guid? FolderId { get; set; } public string? FolderName { get; set; } public int FailedCount { get; set; } public int SuccessCount { get; set; } public int SkippedCount { get; set; } public int Total { get; set; } public DateTimeOffset CreatedAt { get; set; } }
    public sealed class AcknowledgeRequest { public string? Notes { get; set; } }
    public sealed class BatchDocumentActionRequest { public string? Reason { get; set; } public IReadOnlyList<Guid>? OnlySelectedDocumentIds { get; set; } }

    private async Task<bool> CanAccessBatchAsync(Guid batchId, CancellationToken ct)
    {
        var canViewTenant = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT exists(SELECT 1 FROM ged.upload_batch WHERE tenant_id=@tenantId AND id=@batchId AND coalesce(reg_status,'A')='A' AND (@canViewTenant OR created_by=@userId));", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, batchId, canViewTenant }, cancellationToken: ct));
    }


    private async Task<IReadOnlyList<Guid>> LoadBatchDocumentIdsAsync(Guid batchId, IReadOnlyList<Guid>? onlySelectedDocumentIds, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var selected = (onlySelectedDocumentIds ?? Array.Empty<Guid>()).Where(x => x != Guid.Empty).Distinct().ToArray();
        return (await conn.QueryAsync<Guid>(new CommandDefinition("""
SELECT DISTINCT i.document_id
FROM ged.upload_batch_item i
WHERE i.tenant_id=@tenantId AND i.batch_id=@batchId AND coalesce(i.reg_status,'A')='A' AND i.document_id IS NOT NULL
  AND (cardinality(@selected)=0 OR i.document_id = ANY(@selected));
""", new { tenantId = _currentUser.TenantId, batchId, selected }, cancellationToken: ct))).AsList();
    }

    private async Task<IReadOnlyList<GedUploadBatchItemVM>> LoadItemsAsync(System.Data.IDbConnection conn, Guid batchId, CancellationToken ct)
        => (await conn.QueryAsync<GedUploadBatchItemVM>(new CommandDefinition("""
SELECT id, original_file_name AS OriginalFileName, size_bytes AS SizeBytes, status, document_id AS DocumentId, version_id AS VersionId,
       error_message AS ErrorMessage, error_step AS ErrorStep, can_retry AS CanRetry, elapsed_ms AS ElapsedMs, correlation_id AS CorrelationId, processing_warning AS ProcessingWarning
FROM ged.upload_batch_item
WHERE tenant_id=@tenantId AND batch_id=@batchId AND coalesce(reg_status,'A')='A'
ORDER BY created_at, original_file_name;
""", new { tenantId = _currentUser.TenantId, batchId }, cancellationToken: ct))).AsList();

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return '"' + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + '"';
    }
}
