using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebGed.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private const int MaxBulkMoveDocuments = 200;
    private const int MaxReasonLength = 500;
    private static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase) { "SINGLE", "BULK", "MVC", "WEBAPI", "WORKFLOW" };

    private readonly DocumentAppService _svc;
    private readonly IDocumentMoveService _moveService;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(DocumentAppService svc, IDocumentMoveService moveService, ICurrentUser currentUser, ILogger<DocumentsController> logger)
    {
        _svc = svc;
        _moveService = moveService;
        _currentUser = currentUser;
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
    {
        var folders = await _moveService.SearchFoldersAsync(_currentUser.TenantId, _currentUser.UserId, term, ct);
        return Ok(folders);
    }

    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] DocumentMoveRequestVM request, CancellationToken ct)
    {
        if (request.DocumentId == Guid.Empty) return ValidationFailure("Documento inválido.");
        if (request.DestinationFolderId == Guid.Empty) return ValidationFailure("Pasta de destino inválida.");
        if (!ValidateReason(request.Reason, out var reasonError)) return ValidationFailure(reasonError);
        if (!TryNormalizeSource(request.Source, "SINGLE", out var source, out var sourceError)) return ValidationFailure(sourceError);

        try
        {
            var result = await _moveService.MoveAsync(
                _currentUser.TenantId,
                _currentUser.UserId,
                User.Identity?.Name ?? _currentUser.Email,
                request.DocumentId,
                request.DestinationFolderId,
                request.Reason,
                source,
                IsAdmin(),
                ct);

            if (!result.Success) return MapMoveFailure(result.Error?.Code, result.Error?.Message ?? result.ErrorMessage);
            return Ok(new { success = true, message = result.Value!.Message, documentId = result.Value.DocumentId, oldFolderId = result.Value.OldFolderId, newFolderId = result.Value.NewFolderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mover documento Tenant={TenantId} User={UserId} Document={DocumentId} Destination={DestinationFolderId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, request.DocumentId, request.DestinationFolderId, HttpContext.TraceIdentifier);
            return StatusCode(500, new { success = false, message = "Erro interno ao mover documento." });
        }
    }

    [HttpPost("move-bulk")]
    public async Task<IActionResult> MoveBulk([FromBody] DocumentBulkMoveRequestVM request, CancellationToken ct)
    {
        if (request.DocumentIds is null || request.DocumentIds.Count == 0) return ValidationFailure("Informe ao menos um documento.");
        if (request.DocumentIds.Any(id => id == Guid.Empty)) return ValidationFailure("A lista contém documento inválido.");
        var distinctDocumentIds = request.DocumentIds.Distinct().ToArray();
        if (distinctDocumentIds.Length != request.DocumentIds.Count) return ValidationFailure("A lista de documentos não pode conter IDs duplicados.");
        if (distinctDocumentIds.Length > MaxBulkMoveDocuments) return ValidationFailure($"O lote excede o limite máximo de {MaxBulkMoveDocuments} documentos.");
        if (request.DestinationFolderId == Guid.Empty) return ValidationFailure("Pasta de destino inválida.");
        if (!ValidateReason(request.Reason, out var reasonError)) return ValidationFailure(reasonError);
        if (!TryNormalizeSource(request.Source, "BULK", out var source, out var sourceError)) return ValidationFailure(sourceError);

        try
        {
            var result = await _moveService.MoveBulkAsync(
                _currentUser.TenantId,
                _currentUser.UserId,
                User.Identity?.Name ?? _currentUser.Email,
                distinctDocumentIds,
                request.DestinationFolderId,
                request.Reason,
                source,
                IsAdmin(),
                ct);
            if (!result.Success) return MapMoveFailure(result.Error?.Code, result.Error?.Message ?? result.ErrorMessage);
            var v = result.Value!;
            return Ok(new { success = true, batchId = v.BatchId, total = v.Total, successCount = v.SuccessCount, failCount = v.FailCount, items = v.Items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mover lote Tenant={TenantId} User={UserId} Destination={DestinationFolderId} Count={Count} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, request.DestinationFolderId, distinctDocumentIds.Length, HttpContext.TraceIdentifier);
            return StatusCode(500, new { success = false, message = "Erro interno ao mover documentos." });
        }
    }

    [HttpGet("{id:guid}/move-history")]
    public async Task<IActionResult> MoveHistory([FromRoute] Guid id, CancellationToken ct)
        => Ok(await _moveService.GetMoveHistoryAsync(_currentUser.TenantId, id, ct));

    private bool IsAdmin() => DocumentMoveAuthorizationRoles.IsAdministrative(_currentUser.Roles);

    private static bool ValidateReason(string? reason, out string error)
    {
        error = string.Empty;
        if (reason?.Length > MaxReasonLength) { error = $"Motivo deve ter no máximo {MaxReasonLength} caracteres."; return false; }
        return true;
    }

    private static bool TryNormalizeSource(string? source, string fallback, out string normalized, out string error)
    {
        normalized = string.IsNullOrWhiteSpace(source) ? fallback : source.Trim().ToUpperInvariant();
        error = string.Empty;
        if (AllowedSources.Contains(normalized)) return true;
        error = "Origem de movimentação inválida.";
        return false;
    }

    private static IActionResult ValidationFailure(string message) => new BadRequestObjectResult(new { success = false, message });

    private IActionResult MapMoveFailure(string? code, string? message)
    {
        var body = new { success = false, code, message = string.IsNullOrWhiteSpace(message) ? "Falha ao mover documento." : message };
        return (code ?? string.Empty).ToUpperInvariant() switch
        {
            "ACCESS_DENIED" => StatusCode(StatusCodes.Status403Forbidden, body),
            "DOCUMENT_NOT_FOUND" => NotFound(body),
            "DESTINATION_NOT_FOUND" => NotFound(body),
            "INVALID_DESTINATION" => BadRequest(body),
            "VALIDATION" => BadRequest(body),
            "CONFLICT" => Conflict(body),
            _ => StatusCode(StatusCodes.Status500InternalServerError, body)
        };
    }
}
