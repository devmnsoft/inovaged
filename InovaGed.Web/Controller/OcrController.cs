using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ocr")]
public sealed class OcrController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IOcrStatusQueries _ocrStatus;
    private readonly ILogger<OcrController> _logger;

    public OcrController(
        ICurrentUser currentUser,
        IOcrStatusQueries ocrStatus,
        ILogger<OcrController> logger)
    {
        _currentUser = currentUser;
        _ocrStatus = ocrStatus;
        _logger = logger;
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