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
                HttpContext.TraceIdentifier,
                request.IdempotencyKey,
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
                request.AllOrNothing,
                request.IdempotencyKey,
                HttpContext.TraceIdentifier,
                ct);
            if (!result.Success) return MapMoveFailure(result.Error?.Code, result.Error?.Message ?? result.ErrorMessage);
            var v = result.Value!;
            var completed = v.FailCount == 0 && !v.RolledBack;
            return Ok(new { success = completed, batchId = v.BatchId, total = v.Total, successCount = v.SuccessCount, failCount = v.FailCount, allOrNothing = v.AllOrNothing, rolledBack = v.RolledBack, idempotentReplay = v.IdempotentReplay, items = v.Items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao mover lote Tenant={TenantId} User={UserId} Destination={DestinationFolderId} Count={Count} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, request.DestinationFolderId, distinctDocumentIds.Length, HttpContext.TraceIdentifier);
            return StatusCode(500, new { success = false, message = "Erro interno ao mover documentos." });
        }
    }

    [HttpPost("undo-move")]
    public async Task<IActionResult> UndoMove([FromBody] DocumentUndoMoveRequestVM request, CancellationToken ct)
    {
        if (request is null) return ValidationFailure("Informe a movimentação a desfazer.");
        if (!ValidateReason(request.Reason, out var reasonError)) return ValidationFailure(reasonError);
        var entries = UndoEntries(request);
        if (entries.Count == 0) return ValidationFailure("Informe o documento a desfazer.");
        if (entries.Count > MaxBulkMoveDocuments) return ValidationFailure($"O lote excede o limite máximo de {MaxBulkMoveDocuments} documentos.");

        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            var userName = User.Identity?.Name ?? _currentUser.Email;
            if (entries.Count == 1 && request.Items is not { Count: > 0 })
            {
                var result = await _moveService.UndoAsync(_currentUser.TenantId, _currentUser.UserId, userName, entries[0].DocumentId, entries[0].HistoryId, request.Reason, IsAdmin(), correlationId, request.IdempotencyKey, ct);
                if (!result.Success) return MapMoveFailure(result.Error?.Code, result.Error?.Message ?? result.ErrorMessage);
                return Ok(new { success = true, code = result.Value!.ErrorCode, message = result.Value.Message, documentId = result.Value.DocumentId, oldFolderId = result.Value.OldFolderId, newFolderId = result.Value.NewFolderId, historyId = result.Value.HistoryId });
            }

            var bulk = await _moveService.UndoBulkAsync(_currentUser.TenantId, _currentUser.UserId, userName, entries, request.Reason, IsAdmin(), correlationId, request.IdempotencyKey, ct);
            if (!bulk.Success) return MapMoveFailure(bulk.Error?.Code, bulk.Error?.Message ?? bulk.ErrorMessage);
            var value = bulk.Value!;
            var completed = value.FailCount == 0;
            return Ok(new { success = completed, batchId = value.BatchId, total = value.Total, successCount = value.SuccessCount, failCount = value.FailCount, items = value.Items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desfazer movimentação Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, HttpContext.TraceIdentifier);
            return StatusCode(500, new { success = false, message = "Erro interno ao desfazer a movimentação." });
        }
    }

    [HttpGet("{id:guid}/move-history")]
    public async Task<IActionResult> MoveHistory([FromRoute] Guid id, CancellationToken ct)
        => Ok(await _moveService.GetMoveHistoryAsync(_currentUser.TenantId, id, ct));

    private static List<DocumentUndoItemVM> UndoEntries(DocumentUndoMoveRequestVM request)
    {
        if (request.Items is { Count: > 0 })
            return request.Items.Where(item => item.DocumentId != Guid.Empty).GroupBy(item => item.DocumentId).Select(group => group.First()).ToList();
        if (request.DocumentId == Guid.Empty) return [];
        return [new DocumentUndoItemVM { DocumentId = request.DocumentId, HistoryId = request.HistoryId }];
    }

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
            "DOCUMENT_NOT_FOUND" or "DESTINATION_NOT_FOUND" or "FOLDER_NOT_FOUND" or "HISTORY_NOT_FOUND" => NotFound(body),
            "FOLDER_INACTIVE" or "DATABASE_CONSTRAINT_ERROR" or "CONCURRENT_MODIFICATION" or "CONFLICT" or "INVALID_DESTINATION" or "DOCUMENT_LOCKED" => Conflict(body),
            "VALIDATION" or "LIMIT" => BadRequest(body),
            _ => StatusCode(StatusCodes.Status500InternalServerError, body)
        };
    }
}
