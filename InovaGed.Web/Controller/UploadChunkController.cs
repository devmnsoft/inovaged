using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Ged/UploadChunk")]
[DisableRequestSizeLimit]
public sealed class UploadChunkController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IUploadChunkService _chunks;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly IUploadFolderResolver _uploadFolderResolver;
    private readonly ILogger<UploadChunkController> _logger;

    public UploadChunkController(ICurrentUser currentUser, IUploadChunkService chunks, IGedAccessPolicyService accessPolicy, IUploadFolderResolver uploadFolderResolver, ILogger<UploadChunkController> logger)
    {
        _currentUser = currentUser;
        _chunks = chunks;
        _accessPolicy = accessPolicy;
        _uploadFolderResolver = uploadFolderResolver;
        _logger = logger;
    }

    public sealed class StartRequest
    {
        public Guid? BatchId { get; set; }
        public Guid? FolderId { get; set; }
        public Guid? RequestedFolderId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long TotalSizeBytes { get; set; }
        public int? ChunkSizeBytes { get; set; }
        public int? TotalChunks { get; set; }
        public int FileIndex { get; set; }
        public int TotalFiles { get; set; }
        public string? DuplicateStrategy { get; set; }
        public bool RunOcr { get; set; }
        public bool GeneratePreview { get; set; }
        public Guid? ExistingDocumentId { get; set; }
        public string? UploadName { get; set; }
        public DocumentBulkUploadMetadata Metadata { get; set; } = new();
    }

    public sealed class CompleteRequest { public Guid UploadId { get; set; } }

    [HttpPost("Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromBody] StartRequest request, CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        try
        {
            if (request is null) return BadRequest(Error("Dados do upload em partes não foram informados.", "Validação", false, correlationId));
            if (!_currentUser.IsAuthenticated) return Unauthorized(Error("Sua sessão expirou. Faça login novamente.", "Autenticação", false, correlationId));
            var isAdmin = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
            var folder = await ResolveUploadFolderAsync(request.FolderId ?? request.RequestedFolderId, request.RequestedFolderId ?? request.FolderId, isAdmin, correlationId, ct);
            if (!folder.Success) return BadRequest(new { success = false, message = folder.Message, errorStep = "Resolução da pasta", correlationId });
            if (!isAdmin && !await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, folder.ResolvedFolderId, User, ct)) return StatusCode(403, Error("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", false, correlationId));

            var appRequest = new StartUploadChunkRequestDto
            {
                BatchId = request.BatchId,
                FolderId = folder.ResolvedFolderId,
                RequestedFolderId = folder.RequestedFolderId,
                OriginalFileName = request.OriginalFileName,
                ContentType = request.ContentType,
                TotalSizeBytes = request.TotalSizeBytes,
                ChunkSizeBytes = request.ChunkSizeBytes,
                TotalChunks = request.TotalChunks,
                FileIndex = request.FileIndex,
                TotalFiles = request.TotalFiles,
                DuplicateStrategy = request.DuplicateStrategy,
                RunOcr = request.RunOcr,
                GeneratePreview = request.GeneratePreview,
                ExistingDocumentId = request.ExistingDocumentId,
                UploadName = request.UploadName,
                Metadata = request.Metadata ?? new DocumentBulkUploadMetadata(),
                CorrelationId = correlationId
            };

            var result = await _chunks.StartAsync(_currentUser.TenantId, _currentUser.UserId, isAdmin, User.Identity?.Name, appRequest, ct);
            return result.Success ? Ok(new { success = true, session = result.Value, uploadId = result.Value!.UploadId, result.Value.ChunkSizeBytes, result.Value.TotalChunks, result.Value.NextChunk, result.Value.ReceivedChunks, result.Value.MissingChunks, result.Value.Percent, correlationId }) : BadRequest(Error(result.Error?.Message ?? "Falha ao iniciar upload em partes.", result.Error?.Code ?? "UploadChunkStart", true, correlationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao iniciar upload chunked. Tenant={TenantId} User={UserId} File={FileName} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, request?.OriginalFileName, correlationId);
            return StatusCode(500, Error("Não foi possível iniciar upload em partes.", "Servidor", true, correlationId));
        }
    }

    [HttpPost("Part")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue, MultipartHeadersLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Part(Guid uploadId, int chunkIndex, string? checksumSha256, IFormFile chunk, CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        if (!_currentUser.IsAuthenticated) return Unauthorized(Error("Sua sessão expirou. Faça login novamente.", "Autenticação", false, correlationId));
        if (chunk is null || chunk.Length <= 0) return BadRequest(Error("Parte inválida.", "Validação", true, correlationId));
        await using var stream = chunk.OpenReadStream();
        var result = await _chunks.SavePartAsync(_currentUser.TenantId, _currentUser.UserId, new UploadChunkPartRequestDto { UploadId = uploadId, ChunkIndex = chunkIndex, Content = stream, SizeBytes = chunk.Length, ChecksumSha256 = checksumSha256, CorrelationId = correlationId }, ct);
        return result.Success ? Ok(new { success = true, status = result.Value, correlationId }) : BadRequest(Error(result.Error?.Message ?? "Falha ao receber parte.", result.Error?.Code ?? "UploadChunkPart", true, correlationId));
    }

    [HttpPost("Complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete([FromBody] CompleteRequest request, CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var result = await _chunks.CompleteAsync(_currentUser.TenantId, _currentUser.UserId, request.UploadId, ct);
        return result.Success ? Ok(new { success = true, itemId = result.Value!.ItemId, documentId = result.Value.DocumentId, versionId = result.Value.VersionId, status = result.Value.Status, message = result.Value.Message, ocrQueued = result.Value.OcrQueued, previewQueued = result.Value.PreviewQueued, correlationId = result.Value.CorrelationId ?? correlationId }) : BadRequest(Error(result.Error?.Message ?? "Falha ao concluir upload em partes.", result.Error?.Code ?? "UploadChunkComplete", true, correlationId));
    }

    [HttpGet("Status/{uploadId:guid}")]
    public async Task<IActionResult> Status(Guid uploadId, CancellationToken ct)
    {
        var result = await _chunks.GetStatusAsync(_currentUser.TenantId, _currentUser.UserId, uploadId, ct);
        return result.Success ? Ok(new { success = true, status = result.Value }) : BadRequest(Error(result.Error?.Message ?? "Falha ao consultar upload.", result.Error?.Code ?? "UploadChunkStatus", true, HttpContext.TraceIdentifier));
    }

    [HttpPost("Cancel/{uploadId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid uploadId, CancellationToken ct)
    {
        var result = await _chunks.CancelAsync(_currentUser.TenantId, _currentUser.UserId, uploadId, ct);
        return result.Success ? Ok(new { success = true, status = result.Value }) : BadRequest(Error(result.Error?.Message ?? "Falha ao cancelar upload.", result.Error?.Code ?? "UploadChunkCancel", true, HttpContext.TraceIdentifier));
    }

    private async Task<UploadFolderResolutionResult> ResolveUploadFolderAsync(Guid? folderId, Guid? requestedFolderId, bool isAdmin, string correlationId, CancellationToken ct)
    {
        var receivedFolderId = folderId ?? Guid.Empty;
        var resolution = await _uploadFolderResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, receivedFolderId, isAdmin, ct);
        resolution.RequestedFolderId = requestedFolderId ?? receivedFolderId;
        _logger.LogInformation("Upload chunk destino resolvido. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} ReceivedFolderId={ReceivedFolderId} ResolvedFolderId={ResolvedFolderId} Success={Success} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, resolution.RequestedFolderId, receivedFolderId, resolution.ResolvedFolderId, resolution.Success, correlationId);
        return resolution;
    }

    private static object Error(string message, string errorStep, bool canRetry, string correlationId, string? code = null) => new { success = false, message, errorStep, canRetry, correlationId, code };
}
