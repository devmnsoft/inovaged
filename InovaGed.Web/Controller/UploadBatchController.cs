using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
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
    private readonly IFolderQueries _folders;
    private readonly IUploadFolderResolver _uploadFolderResolver;
    private readonly ILogger<UploadBatchController> _logger;

    public UploadBatchController(ICurrentUser currentUser, IUploadBatchService batches, IGedAccessPolicyService accessPolicy, IFolderQueries folders, IUploadFolderResolver uploadFolderResolver, ILogger<UploadBatchController> logger)
    {
        _currentUser = currentUser;
        _batches = batches;
        _accessPolicy = accessPolicy;
        _folders = folders;
        _uploadFolderResolver = uploadFolderResolver;
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
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized(Error("Sua sessão expirou. Faça login novamente.", "Autenticação", false, correlationId));
            if (request is null) return BadRequest(Error("Requisição inválida para iniciar o lote.", "Validação", false, correlationId));
            var isAdmin = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
            var folderResolution = await ResolveUploadFolderAsync(request.FolderId, isAdmin, correlationId, ct);
            if (!folderResolution.Success) return BadRequest(FolderResolutionError(folderResolution, correlationId));
            if (!isAdmin)
            {
                var allowed = await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, folderResolution.ResolvedFolderId, User, ct);
                if (!allowed) return StatusCode(403, Error("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", false, correlationId));
            }

            var result = await _batches.StartAsync(_currentUser.TenantId, _currentUser.UserId, new StartUploadBatchRequestDto
            {
                FolderId = folderResolution.ResolvedFolderId,
                RequestedFolderId = folderResolution.RequestedFolderId,
                TotalFiles = request.TotalFiles,
                Options = request.Options,
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                CorrelationId = correlationId
            }, ct);

            if (!result.Success)
            {
                var code = result.Error?.Code ?? "Validação";
                var isSchemaMissing = string.Equals(code, "UPLOAD_BATCH_SCHEMA_MISSING", StringComparison.OrdinalIgnoreCase);
                return StatusCode(isSchemaMissing ? 500 : 400, Error(
                    isSchemaMissing ? "Upload em lote indisponível. Estrutura de banco pendente." : result.Error?.Message ?? "Não foi possível iniciar o lote.",
                    isSchemaMissing ? "Schema" : "UploadBatchStart",
                    false,
                    correlationId,
                    code));
            }

            return Ok(new { success = true, batchId = result.Value, message = "Lote iniciado.", requestedFolderId = folderResolution.RequestedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, wasVirtual = folderResolution.WasVirtual, createdRealFolder = folderResolution.CreatedRealFolder, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao iniciar lote de upload. Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, correlationId);
            return StatusCode(500, Error("Não foi possível iniciar o lote de upload.", "Servidor", true, correlationId));
        }
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
            var isAdmin = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
            var folderResolution = await ResolveUploadFolderAsync(folderId, isAdmin, correlationId, ct);
            if (!folderResolution.Success) return BadRequest(FolderResolutionError(folderResolution, correlationId));
            if (!isAdmin)
            {
                var allowed = await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, folderResolution.ResolvedFolderId, User, ct);
                if (!allowed) return StatusCode(403, Error("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", false, correlationId));
            }
            var result = await _batches.UploadFileAsync(_currentUser.TenantId, _currentUser.UserId, new UploadBatchFileRequestDto
            {
                BatchId = batchId,
                File = file,
                FileIndex = fileIndex,
                TotalFiles = totalFiles,
                FolderId = folderResolution.ResolvedFolderId,
                RequestedFolderId = folderResolution.RequestedFolderId,
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
                requestedFolderId = folderResolution.RequestedFolderId,
                resolvedFolderId = folderResolution.ResolvedFolderId,
                wasVirtual = folderResolution.WasVirtual,
                createdRealFolder = folderResolution.CreatedRealFolder,
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

    private async Task<UploadFolderResolutionResult> ResolveUploadFolderAsync(Guid? folderId, bool isAdmin, string correlationId, CancellationToken ct)
    {
        var requestedFolderId = folderId ?? Guid.Empty;
        var resolution = await _uploadFolderResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, requestedFolderId, isAdmin, ct);
        _logger.LogInformation("Upload/drop destino resolvido. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} UploadFolderId={UploadFolderId} FolderName={FolderName} CanReceiveDocuments={CanReceiveDocuments} WasDragDrop={WasDragDrop} Source={Source} Success={Success} WasVirtual={WasVirtual} CreatedRealFolder={CreatedRealFolder} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, resolution.RequestedFolderId, resolution.ResolvedFolderId, resolution.FolderName, resolution.Success, true, "local-file", resolution.Success, resolution.WasVirtual, resolution.CreatedRealFolder, correlationId);
        return resolution;
    }

    private static object FolderResolutionError(UploadFolderResolutionResult resolution, string correlationId)
        => new { success = false, message = resolution.Message, errorStep = "Resolução da pasta", canRetry = false, requestedFolderId = resolution.RequestedFolderId, resolvedFolderId = resolution.ResolvedFolderId, wasVirtual = resolution.WasVirtual, createdRealFolder = resolution.CreatedRealFolder, correlationId };

    private static object Error(string message, string errorStep, bool canRetry, string correlationId, string? code = null)
        => new { success = false, message, errorStep, canRetry, correlationId, code };
}
