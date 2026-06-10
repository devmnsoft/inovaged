using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using InovaGed.Web.Security;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("Ocr")]
public sealed class OcrController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IOcrStatusQueries _ocrStatus;
    private readonly IOcrAutoSchedulerService _scheduler;
    private readonly IOcrAutoScheduleRepository _repository;
    private readonly IOptionsMonitor<OcrAutoScheduleOptions> _options;
    private readonly ILogger<OcrController> _logger;

    public OcrController(
        ICurrentUser currentUser,
        IOcrStatusQueries ocrStatus,
        IOcrAutoSchedulerService scheduler,
        IOcrAutoScheduleRepository repository,
        IOptionsMonitor<OcrAutoScheduleOptions> options,
        ILogger<OcrController> logger)
    {
        _currentUser = currentUser;
        _ocrStatus = ocrStatus;
        _scheduler = scheduler;
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    [HttpGet("AutoSchedule")]
    public async Task<IActionResult> AutoSchedule(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var nextRunUtc = OcrAutoScheduleClock.CalculateNextRun(DateTimeOffset.UtcNow, options.RunAt, options.TimeZone);
        var history = await _repository.GetRunHistoryAsync(options.TenantId, 20, ct);
        var eligibleCount = 0;
        try
        {
            eligibleCount = await _repository.CountDocumentsWithoutOcrAsync(options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível contar documentos elegíveis ao OCR automático.");
        }

        var vm = new OcrAutoScheduleDashboardDto
        {
            Enabled = options.Enabled,
            RunAt = options.RunAt,
            TimeZone = options.TimeZone,
            TenantId = options.TenantId,
            MaxDocumentsPerRun = options.MaxDocumentsPerRun,
            BatchSize = options.BatchSize,
            NextRunUtc = nextRunUtc,
            NextRunLocal = OcrAutoScheduleClock.FormatLocal(nextRunUtc, options.TimeZone),
            LastRun = history.FirstOrDefault(),
            EligibleDocumentsCount = eligibleCount,
            History = history.ToList()
        };

        return View("~/Views/Ocr/AutoSchedule.cshtml", vm);
    }

    [HttpPost("AutoSchedule/RunNow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow(CancellationToken ct)
    {
        var result = await _scheduler.RunAsync(ct);
        TempData[result.Status is "SUCCESS" or "PARTIAL_FAILURE" ? "SuccessMessage" : "WarningMessage"] = result.Message ?? "Rotina de OCR automático executada.";
        return RedirectToAction(nameof(AutoSchedule));
    }

    [HttpGet("Status")]
    public async Task<IActionResult> Status([FromQuery] Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (versionId == Guid.Empty)
            return BadRequest(new { success = false, message = "VersionId inválido." });

        try
        {
            var tenantId = _currentUser.TenantId;

            var dict = await _ocrStatus.GetLatestByVersionIdsAsync(
                tenantId,
                new[] { versionId },
                ct);

            if (!dict.TryGetValue(versionId, out var status))
            {
                return Ok(new
                {
                    success = true,
                    exists = false,
                    versionId,
                    status = "NONE",
                    label = "Não executado",
                    isRunning = false,
                    isCompleted = false,
                    isError = false
                });
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
                isError = statusText == "ERROR",
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

            return StatusCode(500, new
            {
                success = false,
                message = "Erro ao consultar status do OCR."
            });
        }
    }

    [HttpGet("StatusMany")]
    public async Task<IActionResult> StatusMany([FromQuery] Guid[] versionIds, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        if (versionIds is null || versionIds.Length == 0)
            return BadRequest(new { success = false, message = "Nenhuma versão informada." });

        try
        {
            var clean = versionIds
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            var dict = await _ocrStatus.GetLatestByVersionIdsAsync(
                _currentUser.TenantId,
                clean,
                ct);

            var result = clean.Select(versionId =>
            {
                if (!dict.TryGetValue(versionId, out var status))
                {
                    return new
                    {
                        versionId,
                        exists = false,
                        jobId = (long?)null,
                        status = "NONE",
                        label = "Não executado",
                        isRunning = false,
                        isCompleted = false,
                        isError = false,
                        requestedAt = (DateTime?)null,
                        startedAt = (DateTime?)null,
                        finishedAt = (DateTime?)null,
                        errorMessage = (string?)null
                    };
                }

                var statusText = status.Status.ToString().ToUpperInvariant();

                return new
                {
                    versionId,
                    exists = true,
                    jobId = (long?)status.JobId,
                    status = statusText,
                    label = Label(statusText),
                    isRunning = statusText is "PENDING" or "PROCESSING",
                    isCompleted = statusText == "COMPLETED",
                    isError = statusText == "ERROR",
                    requestedAt = (DateTime?)status.RequestedAt,
                    startedAt = status.StartedAt,
                    finishedAt = status.FinishedAt,
                    errorMessage = status.ErrorMessage
                };
            }).ToList();

            return Ok(new
            {
                success = true,
                rows = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar múltiplos status OCR.");

            return StatusCode(500, new
            {
                success = false,
                message = "Erro ao consultar status do OCR."
            });
        }
    }

    private static string Label(string status)
    {
        return status switch
        {
            "PENDING" => "Pendente",
            "PROCESSING" => "Executando",
            "COMPLETED" => "Concluído",
            "ERROR" => "Erro",
            _ => "Não executado"
        };
    }
}
