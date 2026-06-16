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

    public GedUploadsController(ICurrentUser currentUser, IDbConnectionFactory db, IUploadBatchService batches, IGedAccessPolicyService accessPolicy, IAuditWriter audit)
    {
        _currentUser = currentUser;
        _db = db;
        _batches = batches;
        _accessPolicy = accessPolicy;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var canViewTenant = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
        await using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<GedUploadBatchRowVM>(new CommandDefinition("""
SELECT b.id, b.created_by AS CreatedBy, coalesce(u.full_name, u.email, u.user_name) AS UserName, b.folder_id AS FolderId, f.name AS FolderName,
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
LEFT JOIN ged.app_user u ON u.id=b.created_by
WHERE b.tenant_id=@tenantId AND coalesce(b.reg_status,'A')='A' AND (@canViewTenant OR b.created_by=@userId)
GROUP BY b.id, u.full_name, u.email, u.user_name, f.name
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
        var batch = await conn.QuerySingleOrDefaultAsync<GedUploadBatchRowVM>(new CommandDefinition("""
SELECT b.id, b.created_by AS CreatedBy, coalesce(u.full_name, u.email, u.user_name) AS UserName, b.folder_id AS FolderId, f.name AS FolderName,
       b.created_at AS CreatedAt, b.finished_at AS FinishedAt, b.status, b.total_files AS TotalFiles, b.success_files AS SuccessFiles, b.failed_files AS FailedFiles, b.skipped_files AS SkippedFiles,
       b.correlation_id AS CorrelationId
FROM ged.upload_batch b
LEFT JOIN ged.folder f ON f.tenant_id=b.tenant_id AND f.id=b.folder_id
LEFT JOIN ged.app_user u ON u.id=b.created_by
WHERE b.tenant_id=@tenantId AND b.id=@batchId AND coalesce(b.reg_status,'A')='A' AND (@canViewTenant OR b.created_by=@userId);
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, batchId, canViewTenant }, cancellationToken: ct));
        if (batch is null) return NotFound();
        var items = await LoadItemsAsync(conn, batchId, ct);
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_DETAIL_VIEW", "UPLOAD_BATCH", batchId, "Detalhe de upload visualizado", null, null, null, ct);
        return View(new GedUploadBatchDetailVM { Batch = batch, Items = items });
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
        return Json(new { success = result.Success, data = result.Value, message = result.Success ? "Falhas liberadas para reenvio." : result.Error?.Message });
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
SELECT id AS BatchId, failed_files AS Failed, total_files AS Total
FROM ged.upload_batch
WHERE tenant_id=@tenantId AND created_by=@userId AND coalesce(reg_status,'A')='A' AND failed_files > 0 AND status IN ('PARTIAL_ERROR','ERROR') AND created_at > now() - interval '24 hours'
ORDER BY created_at DESC LIMIT 1;
""", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId }, cancellationToken: ct));
        var hasProblem = row is not null && row.BatchId != Guid.Empty;
        return Json(new { success = true, hasProblem, batchId = row?.BatchId, failed = row?.Failed ?? 0, total = row?.Total ?? 0, message = hasProblem ? $"Seu último upload teve falhas. {row!.Failed} arquivo(s) não foram enviados." : string.Empty });
    }

    [HttpGet("{batchId:guid}/ExportCsv")]
    public async Task<IActionResult> ExportCsv(Guid batchId, CancellationToken ct)
    {
        if (!await CanAccessBatchAsync(batchId, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        var items = await LoadItemsAsync(conn, batchId, ct);
        var sb = new StringBuilder();
        sb.AppendLine("arquivo,status,mensagem_erro,etapa_erro,document_id,version_id,tamanho_bytes,duracao_ms,correlation_id");
        foreach (var i in items) sb.AppendLine(string.Join(',', Csv(i.OriginalFileName), Csv(i.Status), Csv(i.ErrorMessage), Csv(i.ErrorStep), Csv(i.DocumentId?.ToString()), Csv(i.VersionId?.ToString()), i.SizeBytes?.ToString() ?? "", i.ElapsedMs?.ToString() ?? "", Csv(i.CorrelationId)));
        await _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, "UPLOAD_BATCH_EXPORT_CSV", "UPLOAD_BATCH", batchId, "CSV textual de upload exportado", null, null, new { items.Count }, ct);
        return Content(sb.ToString(), "text/csv", Encoding.UTF8);
    }

    private sealed class LastProblemRow { public Guid BatchId { get; set; } public int Failed { get; set; } public int Total { get; set; } }

    private async Task<bool> CanAccessBatchAsync(Guid batchId, CancellationToken ct)
    {
        var canViewTenant = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT exists(SELECT 1 FROM ged.upload_batch WHERE tenant_id=@tenantId AND id=@batchId AND coalesce(reg_status,'A')='A' AND (@canViewTenant OR created_by=@userId));", new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, batchId, canViewTenant }, cancellationToken: ct));
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
