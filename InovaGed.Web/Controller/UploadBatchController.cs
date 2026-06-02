using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ged/UploadBatch")]
public sealed class UploadBatchController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IUploadBatchService _batches;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly ILogger<UploadBatchController> _logger;

    public UploadBatchController(ICurrentUser currentUser, IUploadBatchService batches, IGedAccessPolicyService accessPolicy, ILogger<UploadBatchController> logger)
    {
        _currentUser = currentUser;
        _batches = batches;
        _accessPolicy = accessPolicy;
        _logger = logger;
    }

    public sealed class StartRequest
    {
        public Guid? FolderId { get; set; }
        public int TotalFiles { get; set; }
        public UploadBatchOptionsDto? Options { get; set; }
    }

    public sealed class FinishRequest
    {
        public Guid BatchId { get; set; }
    }

    [HttpPost("Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromBody] StartRequest request, CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        if (!_currentUser.IsAuthenticated) return Unauthorized(Error("Sua sessão expirou. Faça login novamente.", "Autenticação", false, correlationId));
        if (request.FolderId.HasValue && IsVirtualFolderId(request.FolderId.Value)) return BadRequest(Error("Selecione uma pasta válida antes de enviar documentos.", "Validação de pasta", false, correlationId));
        var allowed = await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, request.FolderId, User, ct);
        if (!allowed) return StatusCode(403, Error("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", false, correlationId));

        var result = await _batches.StartAsync(_currentUser.TenantId, _currentUser.UserId, new StartUploadBatchRequestDto
        {
            FolderId = request.FolderId,
            TotalFiles = request.TotalFiles,
            Options = request.Options,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            CorrelationId = correlationId
        }, ct);
        if (!result.Success) return BadRequest(Error(result.Error?.Message ?? "Não foi possível iniciar o lote.", result.Error?.Code ?? "Validação", true, correlationId));
        return Ok(new { success = true, batchId = result.Value, message = "Lote iniciado.", correlationId });
    }

    [HttpPost("File")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1073741824)]
    public async Task<IActionResult> File(IFormFile file, Guid batchId, int fileIndex, int totalFiles, Guid? folderId, string? duplicateStrategy, bool runOcr, bool generatePreview, Guid? documentTypeId, Guid? classificationId, string? notes, string? visibility, Guid? existingDocumentId, string? uploadName, CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized(Error("Sua sessão expirou. Faça login novamente.", "Autenticação", false, correlationId));
            if (folderId.HasValue && IsVirtualFolderId(folderId.Value)) return BadRequest(Error("Selecione uma pasta válida antes de enviar documentos.", "Validação de pasta", false, correlationId));
            var allowed = await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, folderId, User, ct);
            if (!allowed) return StatusCode(403, Error("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", false, correlationId));

            var isAdmin = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
            var result = await _batches.UploadFileAsync(_currentUser.TenantId, _currentUser.UserId, new UploadBatchFileRequestDto
            {
                BatchId = batchId,
                File = file,
                FileIndex = fileIndex,
                TotalFiles = totalFiles,
                FolderId = folderId,
                DuplicateStrategy = duplicateStrategy,
                RunOcr = runOcr,
                GeneratePreview = generatePreview,
                UploadName = uploadName,
                ExistingDocumentId = existingDocumentId,
                UserName = User.Identity?.Name,
                IsAdmin = isAdmin,
                CorrelationId = correlationId,
                Metadata = new DocumentBulkUploadMetadata { DocumentTypeId = documentTypeId, ClassificationId = classificationId, Notes = notes, Visibility = visibility }
            }, ct);

            if (!result.Success)
            {
                var code = result.Error?.Code ?? "UPLOAD";
                var statusCode = string.Equals(code, "CONCURRENCY", StringComparison.OrdinalIgnoreCase) ? 429 : 400;
                return StatusCode(statusCode, Error(result.Error?.Message ?? "Não foi possível enviar o arquivo.", code == "CONCURRENCY" ? "Concorrência" : code, code != "EXTENSION", correlationId));
            }

            return Ok(new
            {
                success = true,
                itemId = result.Value!.ItemId,
                documentId = result.Value.DocumentId,
                versionId = result.Value.VersionId,
                status = result.Value.Status,
                message = result.Value.Message,
                ocrQueued = result.Value.OcrQueued,
                previewQueued = result.Value.PreviewQueued,
                correlationId = result.Value.CorrelationId
            });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning("Upload cancelado pelo cliente. Tenant={TenantId} User={UserId} Batch={BatchId} File={FileName} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, batchId, file?.FileName, correlationId);
            return StatusCode(499, Error("Upload cancelado/interrompido pelo cliente.", "Conexão", true, correlationId));
        }
    }

    [HttpPost("Finish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish([FromBody] FinishRequest request, CancellationToken ct)
    {
        var result = await _batches.FinishAsync(_currentUser.TenantId, _currentUser.UserId, request.BatchId, ct);
        return result.Success ? Ok(new { success = true, status = result.Value, message = "Lote finalizado." }) : BadRequest(Error(result.Error?.Message ?? "Falha ao finalizar lote.", result.Error?.Code ?? "Batch", true, HttpContext.TraceIdentifier));
    }

    [HttpGet("Status/{batchId:guid}")]
    public async Task<IActionResult> Status(Guid batchId, CancellationToken ct)
    {
        var result = await _batches.GetStatusAsync(_currentUser.TenantId, _currentUser.UserId, batchId, ct);
        return result.Success ? Ok(new { success = true, data = result.Value }) : BadRequest(Error(result.Error?.Message ?? "Falha ao consultar lote.", result.Error?.Code ?? "Batch", true, HttpContext.TraceIdentifier));
    }

    [HttpPost("RetryFailed/{batchId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryFailed(Guid batchId, CancellationToken ct)
    {
        var result = await _batches.RetryFailedAsync(_currentUser.TenantId, _currentUser.UserId, batchId, ct);
        return result.Success ? Ok(new { success = true, data = result.Value, message = "Falhas liberadas para reenvio." }) : BadRequest(Error(result.Error?.Message ?? "Falha ao preparar retentativa.", result.Error?.Code ?? "Batch", true, HttpContext.TraceIdentifier));
    }

    private static bool IsVirtualFolderId(Guid folderId)
        => folderId.ToString("D").StartsWith("f0000000-0000-0000-0000-0000000000", StringComparison.OrdinalIgnoreCase);

    private static object Error(string message, string errorStep, bool canRetry, string correlationId)
        => new { success = false, message, errorStep, canRetry, correlationId };
}
