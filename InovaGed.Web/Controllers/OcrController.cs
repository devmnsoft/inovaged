using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
[Route("Ocr")]
public sealed class OcrController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IOcrStatusQueries _ocrStatus;
    private readonly IOcrDashboardService _dashboard;
    private readonly IOcrJobRepository _ocrJobs;
    private readonly IDbConnectionFactory _db;
    private readonly IOcrAutoSchedulerService _scheduler;
    private readonly IOcrAutoScheduleRepository _repository;
    private readonly IOptionsMonitor<OcrAutoScheduleOptions> _options;
    private readonly IAuditWriter _audit;
    private readonly ILogger<OcrController> _logger;

    public OcrController(
        ICurrentUser currentUser,
        IOcrStatusQueries ocrStatus,
        IOcrDashboardService dashboard,
        IOcrJobRepository ocrJobs,
        IDbConnectionFactory db,
        IOcrAutoSchedulerService scheduler,
        IOcrAutoScheduleRepository repository,
        IOptionsMonitor<OcrAutoScheduleOptions> options,
        IAuditWriter audit,
        ILogger<OcrController> logger)
    {
        _currentUser = currentUser;
        _ocrStatus = ocrStatus;
        _dashboard = dashboard;
        _ocrJobs = ocrJobs;
        _db = db;
        _scheduler = scheduler;
        _repository = repository;
        _options = options;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] OcrDashboardFilter filter, CancellationToken ct)
    {
        await AuditAsync("OCR_DASHBOARD_VIEW", null, null, null, new { filter }, ct);
        var vm = await _dashboard.GetDashboardAsync(_currentUser.TenantId, filter ?? new OcrDashboardFilter(), ct);
        return View("~/Views/Ocr/Index.cshtml", vm);
    }

    [HttpGet("Queue")]
    public async Task<IActionResult> Queue([FromQuery] OcrDashboardFilter filter, CancellationToken ct)
    {
        await AuditAsync("OCR_QUEUE_VIEW", null, null, null, new { filter }, ct);
        var vm = await _dashboard.GetDashboardAsync(_currentUser.TenantId, filter ?? new OcrDashboardFilter(), ct);
        if (WantsJson()) return Json(new { success = true, items = vm.Items });
        return View("~/Views/Ocr/Index.cshtml", vm);
    }

    [HttpGet("/Ged/Processing")]
    [HttpGet("/Ged/Ocr")]
    [HttpGet("/OcrQueue")]
    [HttpGet("/Processing/Ocr")]
    public IActionResult LegacyOcrRoutes() => RedirectToAction(nameof(Index), "Ocr");

    [HttpGet("AutoSchedule")]
    [Authorize(Policy = AppPolicies.SystemAdmin)]
    public async Task<IActionResult> AutoSchedule(CancellationToken ct)
    {
        await AuditAsync("OCR_AUTO_SCHEDULE_VIEW", null, null, null, new { }, ct);
        var options = _options.CurrentValue;
        IReadOnlyList<OcrAutoScheduleRunSummaryDto> history = Array.Empty<OcrAutoScheduleRunSummaryDto>();
        var eligibleCount = 0;
        var warning = string.Empty;

        try
        {
            history = await _repository.GetRunHistoryAsync(options.TenantId == Guid.Empty ? _currentUser.TenantId : options.TenantId, 20, ct);
            eligibleCount = await _repository.CountDocumentsWithoutOcrAsync(options, ct);
        }
        catch (Exception ex) when (ex is PostgresException or InvalidOperationException)
        {
            warning = "Agendamento OCR ainda não configurado. Execute as migrations.";
            _logger.LogWarning(ex, "Não foi possível carregar histórico/configuração do OCR automático.");
        }

        var nextRunUtc = SafeNextRun(options);
        var vm = new OcrAutoScheduleDashboardDto
        {
            Enabled = options.Enabled,
            RunAt = string.IsNullOrWhiteSpace(options.RunAt) ? "18:00" : options.RunAt,
            TimeZone = options.TimeZone,
            TenantId = options.TenantId == Guid.Empty ? _currentUser.TenantId : options.TenantId,
            MaxDocumentsPerRun = options.MaxDocumentsPerRun,
            BatchSize = options.BatchSize,
            NextRunUtc = nextRunUtc ?? DateTimeOffset.UtcNow,
            NextRunLocal = nextRunUtc.HasValue ? OcrAutoScheduleClock.FormatLocal(nextRunUtc.Value, options.TimeZone) : "Indisponível",
            LastRun = history.FirstOrDefault(),
            EligibleDocumentsCount = eligibleCount,
            History = history.ToList()
        };
        if (!string.IsNullOrWhiteSpace(warning)) TempData["WarningMessage"] = warning;

        return View("~/Views/Ocr/AutoSchedule.cshtml", vm);
    }

    [HttpPost("AutoSchedule/RunNow")]
    [Authorize(Policy = AppPolicies.SystemAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAutoScheduleNow(CancellationToken ct)
    {
        await AuditAsync("OCR_AUTO_RUN_NOW", null, null, null, new { action = "run-now" }, ct);
        try
        {
            var result = await _scheduler.RunAsync(ct);
            var payload = new { success = result.Status is "SUCCESS" or "PARTIAL_FAILURE", message = result.Message ?? "Rotina de OCR automático executada.", status = result.Status, enqueued = result.EnqueuedCount };
            if (WantsJson()) return Json(payload);
            TempData[payload.success ? "SuccessMessage" : "WarningMessage"] = payload.message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao executar OCR automático agora.");
            if (WantsJson()) return StatusCode(500, new { success = false, message = "Não foi possível executar OCR automático agora.", details = ex.Message });
            TempData["WarningMessage"] = "Não foi possível executar OCR automático agora.";
        }
        return RedirectToAction(nameof(AutoSchedule));
    }

    [HttpPost("RunForDocument/{documentId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunForDocument(Guid documentId, CancellationToken ct)
    {
        await AuditAsync("OCR_RUN_FOR_DOCUMENT", documentId, null, null, new { documentId }, ct);
        const string sql = """
select current_version_id
from ged.document
where tenant_id = @tenantId
  and id = @documentId
  and coalesce(reg_status, 'A') = 'A'
limit 1;
""";
        await using var conn = await _db.OpenAsync(ct);
        var versionId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, documentId }, cancellationToken: ct));
        if (!versionId.HasValue) return OcrResponse(false, "Documento não encontrado.", statusCode: 404);
        return await EnqueueVersionAsync(versionId.Value, documentId, "OCR enfileirado com sucesso.", false, ct);
    }

    [HttpPost("RunForVersion/{versionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunForVersion(Guid versionId, CancellationToken ct)
    {
        await AuditAsync("OCR_RUN_FOR_VERSION", null, versionId, null, new { versionId }, ct);
        return await EnqueueVersionAsync(versionId, null, "OCR enfileirado com sucesso.", false, ct);
    }

    [HttpPost("Retry/{jobId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(string jobId, CancellationToken ct)
    {
        await AuditAsync("OCR_RETRY", null, null, jobId, new { jobId }, ct);
        const string sql = """
select j.document_version_id
from ged.ocr_job j
where j.tenant_id = @tenantId
  and j.id::text = @jobId
  and upper(j.status::text) in ('ERROR','FAILED','FAILURE')
limit 1;
""";
        await using var conn = await _db.OpenAsync(ct);
        var versionId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, jobId }, cancellationToken: ct));
        if (!versionId.HasValue) return OcrResponse(false, "Job de OCR com erro não encontrado.", statusCode: 404);
        return await EnqueueVersionAsync(versionId.Value, null, "OCR reenfileirado com sucesso.", true, ct);
    }

    [HttpGet("Job/{jobId}")]
    public async Task<IActionResult> Job(string jobId, CancellationToken ct)
    {
        var job = await _dashboard.GetJobAsync(_currentUser.TenantId, jobId, ct);
        await AuditAsync("OCR_ERROR_DETAILS_VIEW", job?.DocumentId, job?.VersionId, jobId, new { jobId, found = job is not null }, ct);
        if (job is null) return NotFound(new { success = false, message = "Job OCR não encontrado." });
        return Json(new { success = true, job });
    }

    [HttpGet("Document/{documentId:guid}")]
    public IActionResult Document(Guid documentId) => Redirect($"/Ged/Details/{documentId}");

    [HttpGet("Version/{versionId:guid}/Text")]
    public async Task<IActionResult> VersionText(Guid versionId, CancellationToken ct)
    {
        await AuditAsync("OCR_TEXT_VIEW", null, versionId, null, new { versionId }, ct);
        var text = await _dashboard.GetOcrTextByVersionAsync(_currentUser.TenantId, versionId, ct);
        if (text is null) return NotFound(new { success = false, message = "Texto OCR não encontrado." });
        if (WantsJson()) return Json(new { success = true, versionId, hasText = !string.IsNullOrWhiteSpace(text), text });
        return Content(text, "text/plain; charset=utf-8");
    }

    [HttpGet("Status")]
    public async Task<IActionResult> Status([FromQuery] Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (versionId == Guid.Empty) return BadRequest(new { success = false, message = "VersionId inválido." });

        try
        {
            var dict = await _ocrStatus.GetLatestByVersionIdsAsync(_currentUser.TenantId, new[] { versionId }, ct);
            if (!dict.TryGetValue(versionId, out var status))
            {
                return Ok(new { success = true, exists = false, versionId, status = "NONE", label = Label("NONE"), isRunning = false, isCompleted = false, isError = false });
            }

            var statusText = status.Status.ToString().ToUpperInvariant();
            return Ok(new
            {
                success = true,
                exists = true,
                versionId = status.VersionId,
                jobId = status.JobId,
                status = statusText,
                label = Label(statusText),
                isRunning = statusText is "PENDING" or "PROCESSING",
                isCompleted = statusText == "COMPLETED",
                isError = statusText is "ERROR" or "FAILED" or "FAILURE",
                requestedAt = status.RequestedAt,
                startedAt = status.StartedAt,
                finishedAt = status.FinishedAt,
                errorMessage = status.ErrorMessage,
                invalidateDigitalSignatures = status.InvalidateDigitalSignatures
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar status OCR. VersionId={VersionId}", versionId);
            return StatusCode(500, new { success = false, message = "Erro ao consultar status do OCR." });
        }
    }

    [HttpGet("StatusMany")]
    public async Task<IActionResult> StatusMany([FromQuery] Guid[] versionIds, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (versionIds is null || versionIds.Length == 0) return BadRequest(new { success = false, message = "Nenhuma versão informada." });

        try
        {
            var clean = versionIds.Where(x => x != Guid.Empty).Distinct().ToArray();
            var dict = await _ocrStatus.GetLatestByVersionIdsAsync(_currentUser.TenantId, clean, ct);
            var result = clean.Select(versionId =>
            {
                if (!dict.TryGetValue(versionId, out var status))
                {
                    return new { versionId, exists = false, jobId = (long?)null, status = "NONE", label = Label("NONE"), isRunning = false, isCompleted = false, isError = false, requestedAt = (DateTime?)null, startedAt = (DateTime?)null, finishedAt = (DateTime?)null, errorMessage = (string?)null };
                }
                var statusText = status.Status.ToString().ToUpperInvariant();
                return new { versionId, exists = true, jobId = (long?)status.JobId, status = statusText, label = Label(statusText), isRunning = statusText is "PENDING" or "PROCESSING", isCompleted = statusText == "COMPLETED", isError = statusText is "ERROR" or "FAILED" or "FAILURE", requestedAt = (DateTime?)status.RequestedAt, startedAt = status.StartedAt, finishedAt = status.FinishedAt, errorMessage = status.ErrorMessage };
            }).ToList();
            return Ok(new { success = true, rows = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar múltiplos status OCR.");
            return StatusCode(500, new { success = false, message = "Erro ao consultar status do OCR." });
        }
    }

    private async Task<IActionResult> EnqueueVersionAsync(Guid versionId, Guid? documentId, string successMessage, bool forceReprocess, CancellationToken ct)
    {
        if (!await VersionBelongsToTenantAsync(versionId, ct)) return OcrResponse(false, "Versão de documento não encontrada.", statusCode: 404);
        if (await HasPendingOrProcessingAsync(versionId, ct)) return OcrResponse(false, "OCR já está pendente ou em processamento para este documento.", statusCode: 409);

        try
        {
            var jobId = await _ocrJobs.EnqueueAsync(_currentUser.TenantId, versionId, _currentUser.UserId, forceReprocess, ct);
            return OcrResponse(true, successMessage, jobId: jobId.ToString(), documentId: documentId, versionId: versionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enfileirar OCR. VersionId={VersionId}", versionId);
            return OcrResponse(false, "Não foi possível enfileirar OCR.", ex.Message, 500);
        }
    }

    private async Task<bool> HasPendingOrProcessingAsync(Guid versionId, CancellationToken ct)
    {
        const string sql = """
select exists (
    select 1
    from ged.ocr_job
    where tenant_id = @tenantId
      and document_version_id = @versionId
      and upper(status::text) in ('PENDING','PROCESSING')
);
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct));
    }

    private async Task<bool> VersionBelongsToTenantAsync(Guid versionId, CancellationToken ct)
    {
        const string sql = """
select exists (
    select 1
    from ged.document_version v
    join ged.document d on d.tenant_id = v.tenant_id and d.id = v.document_id
    where v.tenant_id = @tenantId
      and v.id = @versionId
      and coalesce(d.reg_status, 'A') = 'A'
);
""";
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { tenantId = _currentUser.TenantId, versionId }, cancellationToken: ct));
    }

    private IActionResult OcrResponse(bool success, string message, string? details = null, int statusCode = 200, string? jobId = null, Guid? documentId = null, Guid? versionId = null)
    {
        var payload = new { success, message, details, jobId, documentId, versionId };
        if (WantsJson()) return StatusCode(statusCode, payload);
        TempData[success ? "SuccessMessage" : "WarningMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    private bool WantsJson()
        => Request.Headers.Accept.Any(x => x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
           || string.Equals(Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private DateTimeOffset? SafeNextRun(OcrAutoScheduleOptions options)
    {
        try { return OcrAutoScheduleClock.CalculateNextRun(DateTimeOffset.UtcNow, options.RunAt, options.TimeZone); }
        catch { return null; }
    }

    private Task AuditAsync(string action, Guid? documentId, Guid? versionId, string? jobId, object data, CancellationToken ct)
        => _audit.WriteAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            action,
            documentId.HasValue ? "DOCUMENT" : "OCR",
            documentId,
            "Ação na Central OCR",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            new { tenantId = _currentUser.TenantId, userId = _currentUser.UserId, documentId, versionId, jobId, action, correlationId = HttpContext.TraceIdentifier, timestampUtc = DateTimeOffset.UtcNow, data },
            ct);

    private static string Label(string status)
        => status switch
        {
            "PENDING" => "Pendente",
            "PROCESSING" => "Executando",
            "COMPLETED" => "Concluído",
            "ERROR" or "FAILED" or "FAILURE" => "Erro",
            "CANCELLED" or "CANCELED" => "Cancelado",
            _ => "Não executado"
        };
}
