using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebGed.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private readonly DocumentAppService _svc;
    private readonly IDocumentMoveService _moveService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(DocumentAppService svc, IDocumentMoveService moveService, ILogger<DocumentsController> logger)
    {
        _svc = svc;
        _moveService = moveService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadDocumentCommand cmd, CancellationToken ct)
    {
        var result = await _svc.UploadAsync(cmd, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", Request.Headers.UserAgent.ToString(), ct);
        if (!result.Success) return BadRequest(result.Error);
        return Ok(new { documentId = result.Value });
    }

    [HttpGet("folders/search")]
    public async Task<IActionResult> SearchFolders([FromQuery] string? term, CancellationToken ct)
        => Ok(await _moveService.SearchFoldersAsync(CurrentTenantId(), term, ct));

    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] DocumentMoveRequestVM request, CancellationToken ct)
    {
        try
        {
            var result = await _moveService.MoveAsync(CurrentTenantId(), CurrentUserId(), User.Identity?.Name, request.DocumentId, request.DestinationFolderId, request.Reason, request.Source ?? "SINGLE", ct);
            if (!result.Success)
            {
                if (string.Equals(result.Error?.Code, "ACCESS_DENIED", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(403, new { success = false, message = result.Error?.Message ?? "Acesso negado." });
                return BadRequest(new { success = false, message = result.Error?.Message ?? result.ErrorMessage });
            }
            return Ok(new { success = true, message = result.Value!.Message, documentId = result.Value.DocumentId, oldFolderId = result.Value.OldFolderId, newFolderId = result.Value.NewFolderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mover documento Tenant={TenantId} User={UserId} Document={DocumentId} Destination={DestinationFolderId}", CurrentTenantId(), CurrentUserId(), request.DocumentId, request.DestinationFolderId);
            return StatusCode(500, new { success = false, message = "Erro interno ao mover documento." });
        }
    }

    [HttpPost("move-bulk")]
    public async Task<IActionResult> MoveBulk([FromBody] DocumentBulkMoveRequestVM request, CancellationToken ct)
    {
        var result = await _moveService.MoveBulkAsync(CurrentTenantId(), CurrentUserId(), User.Identity?.Name, request.DocumentIds, request.DestinationFolderId, request.Reason, request.Source ?? "BULK", ct);
        if (!result.Success) return BadRequest(new { success = false, message = result.Error?.Message ?? result.ErrorMessage });
        var v = result.Value!;
        return Ok(new { success = true, batchId = v.BatchId, total = v.Total, successCount = v.SuccessCount, failCount = v.FailCount, items = v.Items });
    }

    [HttpGet("{id:guid}/move-history")]
    public async Task<IActionResult> MoveHistory([FromRoute] Guid id, CancellationToken ct)
        => Ok(await _moveService.GetMoveHistoryAsync(CurrentTenantId(), id, ct));

    private Guid CurrentTenantId() => Guid.Parse(User.FindFirst("tenant_id")!.Value);
    private Guid CurrentUserId() => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}
