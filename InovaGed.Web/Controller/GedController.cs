using System.Globalization;
using System.Text;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Search;
using InovaGed.Application.Audit;
using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Preview;
using InovaGed.Application.Search;
using InovaGed.Application.Security;
using InovaGed.Application.Workflow;
using InovaGed.Domain.Ged;
using InovaGed.Domain.Primitives;
using InovaGed.Web.Models.Ged;
using InovaGed.Web.ocr;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using Npgsql;
using InovaGed.Web.Services;
using InovaGed.Application.Ged.Documents.Partials;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class GedController : Controller
{
    private readonly ILogger<GedController> _logger;
    private readonly ICurrentUser _currentUser;

    private readonly IDbConnectionFactory _db;

    private readonly IDocumentCommands _documentCommands;
    private readonly IOcrSignalRNotifier _ocrNotifier;

    private readonly IFolderQueries _folders;
    private readonly IFolderCommands _folderCommands;

    private readonly IOcrJobRepository _ocrJobs;
    private readonly IOcrStatusQueries _ocrStatus; // (mantido)

    private readonly IDocumentQueries _docs;
    private readonly DocumentAppService _documentApp;
    private readonly IFileStorage _storage;
    private readonly IPreviewGenerator _preview;
    private readonly IPreviewJobQueue _previewQueue;
    private readonly IPreviewStatusRepository _previewStatus;

    private readonly IDocumentSearchQueries _search;
    private readonly IDocumentMoveService _documentMoveService;
    private readonly IDocumentBulkUploadService _documentBulkUploadService;
    private readonly IGedAccessPolicyService _accessPolicy;
    private readonly IUploadFolderResolver _uploadFolderResolver;
    private readonly IFolderNavigationResolver _folderNavigationResolver;
    private readonly IGedSmartSearchService _smartSearch;
    private readonly IAuditWriter _auditWriter;
    private readonly IDateTimeDisplayService _dateTimeDisplay;
    private readonly IDocumentPartialService _documentPartialService;

    // ✅ classificação
    private readonly IDocumentClassificationQueries _clsQ;

    // Workflow
    private readonly IWorkflowQueries _workflowQ;
    private readonly IWorkflowCommands _workflowC;
    private readonly IDocumentWorkflowQueries _docWfQ;
    private readonly IDocumentWorkflowCommands _docWfC;

    public GedController(
        ILogger<GedController> logger,
        ICurrentUser currentUser,
        IDbConnectionFactory db,                    // ✅ ADICIONADO
        IFolderQueries folders,
        IOcrStatusQueries ocrStatus,
        IDocumentSearchQueries search,
        IDocumentMoveService documentMoveService,
        IDocumentBulkUploadService documentBulkUploadService,
        IGedAccessPolicyService accessPolicy,
        IUploadFolderResolver uploadFolderResolver,
        IFolderNavigationResolver folderNavigationResolver,
        IGedSmartSearchService smartSearch,
        IAuditWriter auditWriter,
        IOcrJobRepository ocrJobs,
        IFolderCommands folderCommands,
        IDocumentQueries docs,
        DocumentAppService documentApp,
        IFileStorage storage,
        IPreviewGenerator preview,
        IPreviewJobQueue previewQueue,
        IPreviewStatusRepository previewStatus,
        IWorkflowQueries workflowQ,
        IWorkflowCommands workflowC,
        IDocumentWorkflowQueries docWfQ,
        IDocumentWorkflowCommands docWfC,
        IDocumentClassificationQueries clsQ,
        IDocumentCommands documentCommands,
        IOcrSignalRNotifier ocrNotifier,
        IDateTimeDisplayService dateTimeDisplay,
        IDocumentPartialService documentPartialService)
    {
        _logger = logger;
        _currentUser = currentUser;
        _db = db;

        _folders = folders;
        _folderCommands = folderCommands;

        _ocrJobs = ocrJobs;
        _ocrStatus = ocrStatus;

        _search = search;
        _documentMoveService = documentMoveService;
        _documentBulkUploadService = documentBulkUploadService;
        _accessPolicy = accessPolicy;
        _uploadFolderResolver = uploadFolderResolver;
        _folderNavigationResolver = folderNavigationResolver;
        _smartSearch = smartSearch;
        _auditWriter = auditWriter;
        _dateTimeDisplay = dateTimeDisplay;
        _documentPartialService = documentPartialService;

        _docs = docs;
        _documentApp = documentApp;
        _storage = storage;
        _preview = preview;
        _previewQueue = previewQueue;
        _previewStatus = previewStatus;

        _workflowQ = workflowQ;
        _workflowC = workflowC;
        _docWfQ = docWfQ;
        _docWfC = docWfC;

        _clsQ = clsQ;

        _documentCommands = documentCommands;
        _ocrNotifier = ocrNotifier;
    }

    [HttpPost("/Ged/Documents/BulkUploadSingle")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(52428800)]
    public async Task<IActionResult> BulkUploadSingle(IFormFile file, Guid? folderId, Guid? uploadFolderId, Guid? requestedFolderId, Guid? documentTypeId, Guid? classificationId, string? notes, string? visibility, bool runOcr, bool generatePreview, Guid? batchId, string? duplicateStrategy, Guid? existingDocumentId, string? uploadName, bool isDocumentPart, int? partNumber, int? totalParts, Guid? consolidateIntoDocumentId, int? fileIndex, int? totalFiles, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = HttpContext.TraceIdentifier;
        try
        {
            _logger.LogInformation("Bulk upload iniciado. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} FileIndex={FileIndex} TotalFiles={TotalFiles} File={FileName} Size={FileSize} RunOcr={RunOcr} GeneratePreview={GeneratePreview} ConnectionId={ConnectionId} CorrelationId={CorrelationId}",
                _currentUser.TenantId, _currentUser.UserId, uploadFolderId ?? folderId, batchId, fileIndex, totalFiles, file?.FileName, file?.Length, runOcr, generatePreview, HttpContext.Connection.Id, correlationId);
            if (!_currentUser.IsAuthenticated) return Unauthorized(JsonError("Sua sessão expirou. Faça login novamente.", "Autenticação", "Usuário sem sessão autenticada.", false, correlationId));
            if (isDocumentPart && !CanAddDocumentPart())
            {
                return StatusCode(403, JsonError("Você não possui permissão para marcar documentos como incompletos ou adicionar partes.", "Autorização", "Permissão DOCUMENT_PART_ADD/DOCUMENT_PART_MARK_INCOMPLETE negada.", false, correlationId));
            }
            var isAdmin = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
            var receivedFolderId = uploadFolderId ?? folderId;
            var folderResolution = await ResolveUploadFolderAsync(receivedFolderId, requestedFolderId, isAdmin, correlationId, ct);
            if (!folderResolution.Success) return BadRequest(FolderResolutionJsonError(folderResolution, correlationId));
            if (!isAdmin)
            {
                var allowed = await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, folderResolution.ResolvedFolderId, User, ct);
                if (!allowed)
                {
                    return StatusCode(403, JsonError("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", "Permissão negada para upload na pasta selecionada.", false, correlationId));
                }
            }
            _logger.LogInformation("Bulk upload validações concluídas. RequestedFolderId={RequestedFolderId} ResolvedFolderId={ResolvedFolderId} WasVirtual={WasVirtual} CreatedRealFolder={CreatedRealFolder} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}", folderResolution.RequestedFolderId, folderResolution.ResolvedFolderId, folderResolution.WasVirtual, folderResolution.CreatedRealFolder, sw.ElapsedMilliseconds, correlationId);

            var metadata = new DocumentBulkUploadMetadata { DocumentTypeId = documentTypeId, ClassificationId = classificationId, Notes = notes, Visibility = visibility, RunOcr = runOcr, GeneratePreview = generatePreview, BatchId = batchId, DuplicateStrategy = duplicateStrategy, ExistingDocumentId = existingDocumentId, UploadName = uploadName, IsDocumentPart = isDocumentPart, PartNumber = partNumber, TotalParts = totalParts, ConsolidateIntoDocumentId = consolidateIntoDocumentId };
            var result = await _documentBulkUploadService.UploadSingleAsync(_currentUser.TenantId, _currentUser.UserId, User.Identity?.Name, file, folderResolution.ResolvedFolderId, metadata, isAdmin, ct);
            _logger.LogInformation("Bulk upload persistência concluída. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} FileIndex={FileIndex} TotalFiles={TotalFiles} File={FileName} Success={Success} ElapsedMs={ElapsedMs} ConnectionId={ConnectionId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, folderId, batchId, fileIndex, totalFiles, file?.FileName, result.Success, sw.ElapsedMilliseconds, HttpContext.Connection.Id, correlationId);
            if (!result.Success)
            {
                var code = result.Error?.Code ?? string.Empty;
                var message = result.Error?.Message ?? "Não foi possível enviar o arquivo.";
                var isExtensionError = code.Contains("EXT", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("extensão", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("extension", StringComparison.OrdinalIgnoreCase);

                return BadRequest(JsonError(message, isExtensionError ? "Validação de extensão" : "Persistência", string.IsNullOrWhiteSpace(code) ? "Falha ao processar upload no backend." : code, !isExtensionError, correlationId));
            }
            var uploadedDocument = await _docs.GetAsync(_currentUser.TenantId, result.Value.DocumentId, ct);
            var versionId = result.Value.VersionId ?? uploadedDocument?.CurrentVersionId;
            if (result.Value.DocumentId == Guid.Empty || !versionId.HasValue || versionId.Value == Guid.Empty)
            {
                _logger.LogError("Upload retornou documento/versão inválidos. Tenant={TenantId} DocumentId={DocumentId} VersionId={VersionId} CorrelationId={CorrelationId}", _currentUser.TenantId, result.Value.DocumentId, versionId, correlationId);
                return StatusCode(500, JsonError("Arquivo salvo, mas a versão atual não foi localizada. Atualize a pasta e tente abrir novamente.", "Persistência", "DocumentId/VersionId inválidos após upload.", true, correlationId));
            }
            return Ok(new { success = true, status = "success", message = "Arquivo enviado com sucesso.", documentId = result.Value.DocumentId, versionId, requestedFolderId = folderResolution.RequestedFolderId, folderId = folderResolution.ResolvedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, folderName = folderResolution.FolderName, createdDocuments = new[] { new { documentId = result.Value.DocumentId, versionId, title = result.Value.Title, fileName = result.Value.FileName } }, data = new { documentId = result.Value.DocumentId, versionId, title = result.Value.Title, fileName = result.Value.FileName, batchId, ocrQueued = runOcr, previewQueued = generatePreview, requestedFolderId = folderResolution.RequestedFolderId, folderId = folderResolution.ResolvedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, folderName = folderResolution.FolderName, wasVirtual = folderResolution.WasVirtual, createdRealFolder = folderResolution.CreatedRealFolder }, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no BulkUploadSingle. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, folderId, batchId, correlationId);
            return StatusCode(500, JsonError("Erro interno ao enviar arquivo.", "Servidor", ex.Message, true, correlationId));
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > 30_000)
            {
                _logger.LogError("Upload crítico: duração acima de 30s. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} FileIndex={FileIndex} TotalFiles={TotalFiles} File={FileName} Size={FileSize} ContentType={ContentType} DuplicateStrategy={DuplicateStrategy} RunOcr={RunOcr} GeneratePreview={GeneratePreview} ElapsedMs={ElapsedMs} ConnectionId={ConnectionId} CorrelationId={CorrelationId}",
                    _currentUser.TenantId, _currentUser.UserId, folderId, batchId, fileIndex, totalFiles, file?.FileName, file?.Length, file?.ContentType, duplicateStrategy, runOcr, generatePreview, sw.ElapsedMilliseconds, HttpContext.Connection.Id, correlationId);
            }
            else if (sw.ElapsedMilliseconds > 10_000)
            {
                _logger.LogWarning("Upload lento: duração acima de 10s. Tenant={TenantId} User={UserId} Folder={FolderId} Batch={BatchId} FileIndex={FileIndex} TotalFiles={TotalFiles} File={FileName} Size={FileSize} ContentType={ContentType} DuplicateStrategy={DuplicateStrategy} RunOcr={RunOcr} GeneratePreview={GeneratePreview} ElapsedMs={ElapsedMs} ConnectionId={ConnectionId} CorrelationId={CorrelationId}",
                    _currentUser.TenantId, _currentUser.UserId, folderId, batchId, fileIndex, totalFiles, file?.FileName, file?.Length, file?.ContentType, duplicateStrategy, runOcr, generatePreview, sw.ElapsedMilliseconds, HttpContext.Connection.Id, correlationId);
            }
        }
    }


    [HttpGet("/Ged/DocumentsList")]
    public async Task<IActionResult> DocumentsList(Guid? folderId, string? q, int page = 1, int pageSize = 50, string? format = null, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var tenantId = _currentUser.TenantId;
        var isAdmin = await _accessPolicy.IsAdminAsync(tenantId, _currentUser.UserId, User, ct);
        var resolved = await _folderNavigationResolver.ResolveForListingAsync(tenantId, _currentUser.UserId, folderId, isAdmin, ct);
        if (!resolved.Success)
        {
            _logger.LogWarning("DocumentsList sem pasta resolvida. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId}", tenantId, _currentUser.UserId, folderId);
            return PartialView("_DocumentsList", new GedExplorerVM { CurrentFolderId = folderId, CurrentFolderName = "Pasta selecionada", Query = q });
        }

        var listingFolderId = resolved.ListingFolderId;
        var docs = await _docs.ListAsync(tenantId, listingFolderId, q, ct);
        var total = docs.Count;
        var pagedDocs = docs.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var hasMore = (page * pageSize) < total;
        _logger.LogInformation("DocumentsList carregado. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} ListingFolderId={ListingFolderId} Count={Count} Page={Page} PageSize={PageSize}", tenantId, _currentUser.UserId, folderId, listingFolderId, total, page, pageSize);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                items = pagedDocs.Select(d => new
                {
                    documentId = d.Id,
                    versionId = d.CurrentVersionId,
                    d.Title,
                    d.FileName,
                    d.TypeName,
                    uploadAt = d.UploadedAtUtc == default ? d.CreatedAt : d.UploadedAtUtc,
                    d.OcrStatus,
                    d.IsOcrAvailable,
                    d.IsDocumentIncomplete
                }),
                total,
                page,
                pageSize,
                hasMore
            });
        }

        var vm = new GedExplorerVM
        {
            CurrentFolderId = resolved.VisualFolderId,
            CurrentListingFolderId = listingFolderId,
            CurrentUploadFolderId = resolved.UploadFolderId,
            CurrentFolderIsVirtual = resolved.WasVirtual,
            CurrentFolderName = resolved.FolderName,
            FolderId = listingFolderId,
            Query = q,
            Page = page,
            PageSize = pageSize,
            TotalDocuments = total,
            HasMoreDocuments = hasMore,
            Documents = pagedDocs.Select(MapDocumentRow).ToList()
        };

        return PartialView("_DocumentsList", vm);
    }


    [HttpGet("/Ged/DocumentPanel")]
    public async Task<IActionResult> DocumentPanel(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var doc = await _docs.GetAsync(tenantId, id, ct);
        if (doc is null) return NotFound();

        var versions = await _docs.ListVersionsAsync(tenantId, id, ct);
        var current = versions.FirstOrDefault(v => v.Id == doc.CurrentVersionId) ?? versions.FirstOrDefault();
        if (current is null) return NotFound();

        var folderName = await ResolveFolderNameAsync(tenantId, doc.FolderId, ct);
        var typeName = await ResolveDocumentTypeNameAsync(tenantId, doc.TypeId, ct);
        var createdByName = await ResolveUserNameAsync(tenantId, current.CreatedBy ?? doc.CreatedBy, ct);
        var canMove = await _accessPolicy.CanMoveDocumentAsync(tenantId, _currentUser.UserId, id, User, ct);
        var canAddPart = CanAddDocumentPart();
        var canViewParts = CanViewDocumentParts();
        var canConsolidate = CanConsolidateDocumentParts() && (current.IsDocumentIncomplete || current.IsPartialDocument || string.Equals(current.PartialStatus, "COMPLETE", StringComparison.OrdinalIgnoreCase));
        var canCancelPartial = CanCancelDocumentParts() && (current.IsDocumentIncomplete || current.IsPartialDocument || !string.Equals(current.PartialStatus, "NOT_PARTIAL", StringComparison.OrdinalIgnoreCase));
        var normalizedOcrStatus = string.IsNullOrWhiteSpace(current.OcrStatus) ? "NONE" : current.OcrStatus.Trim().ToUpperInvariant();
        var ocrText = string.Equals(normalizedOcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? await TryReadOcrTextAsync(tenantId, id, current.Id, ct)
            : string.Empty;
        var isOcrAvailable = string.Equals(normalizedOcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ocrText);
        var parts = (current.IsDocumentIncomplete || current.IsPartialDocument || !string.Equals(current.PartialStatus, "NOT_PARTIAL", StringComparison.OrdinalIgnoreCase)) && canViewParts
            ? await _documentPartialService.GetPartsAsync(tenantId, id, ct)
            : Array.Empty<DocumentPartialPartDto>();
        var history = await LoadDocumentHistoryAsync(tenantId, id, 20, ct);

        var vm = new DocumentSidePanelVm
        {
            DocumentId = id,
            VersionId = current.Id,
            Title = doc.Title,
            FileName = current.FileName,
            TypeName = typeName,
            FolderName = folderName,
            ClassificationName = typeName,
            VersionNumber = current.VersionNumber,
            UploadedAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(current.UploadedAtUtc == default ? current.CreatedAt : current.UploadedAtUtc),
            CreatedAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(doc.CreatedAt),
            CreatedByName = createdByName,
            SizeBytesFormatted = FormatBytes(current.SizeBytes),
            Extension = ResolveFileExtension(current.FileName, current.FileExtension),
            DocumentStatus = doc.Status.ToString(),
            Visibility = string.IsNullOrWhiteSpace(doc.Visibility) ? (doc.IsConfidential ? "CONFIDENTIAL" : "Padrão") : doc.Visibility,
            OcrStatus = normalizedOcrStatus,
            IsOcrAvailable = isOcrAvailable,
            OcrBadgeText = GetOcrBadgeText(normalizedOcrStatus, isOcrAvailable),
            OcrBadgeCss = !string.IsNullOrWhiteSpace(current.OcrSummaryCss) ? current.OcrSummaryCss : (isOcrAvailable ? "bg-success" : normalizedOcrStatus switch { "ERROR" or "FAILED" => "bg-danger", "PROCESSING" or "RUNNING" => "bg-info text-dark", "PENDING" or "QUEUED" => "bg-warning text-dark", _ => "bg-secondary" }),
            PartialPartsWithOcrCount = current.PartialPartsWithOcrCount,
            PartialPartsWithoutOcrCount = current.PartialPartsWithoutOcrCount,
            PartialOcrSummaryText = current.OcrSummaryText,
            PartialOcrSummaryCss = string.IsNullOrWhiteSpace(current.OcrSummaryCss) ? "bg-secondary" : current.OcrSummaryCss,
            IsPartialDocument = current.IsPartialDocument,
            IsDocumentIncomplete = current.IsDocumentIncomplete,
            PartialStatus = current.PartialStatus,
            PartialStatusLabel = current.PartialStatusLabel,
            PartialPartsCount = current.PartialPartsCount,
            PartialTotalParts = current.TotalParts ?? current.PartialTotalParts,
            PreviewUrl = Url.Action(nameof(Preview), "Ged", new { id = current.Id }) ?? string.Empty,
            OcrTextUrl = Url.Action(nameof(DocumentOcrText), "Ged", new { versionId = current.Id }) ?? string.Empty,
            DownloadUrl = Url.Action(nameof(Download), "Ged", new { id = current.Id }) ?? string.Empty,
            DetailsUrl = Url.Action(nameof(Details), "Ged", new { id }) ?? string.Empty,
            PartsUrl = $"/Ged/DocumentParts?id={id}",
            HistoryUrl = Url.Action(nameof(DocumentHistory), "Ged", new { id }) ?? string.Empty,
            CanMove = canMove,
            CanClassify = true,
            CanAddPart = canAddPart,
            CanViewParts = canViewParts,
            CanConsolidate = canConsolidate,
            CanRunOcr = true,
            CanReprocessOcr = string.Equals(normalizedOcrStatus, "ERROR", StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedOcrStatus, "FAILED", StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedOcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase),
            CanCancelPartial = canCancelPartial,
            CanDelete = User.IsInRole(AppRoles.Admin),
            Parts = parts.Select(p => new DocumentSidePanelPartVm
            {
                VersionId = p.VersionId,
                PartNumber = p.PartNumber,
                TotalParts = p.TotalParts,
                FileName = p.FileName ?? string.Empty,
                UploadedAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(p.UploadedAtUtc),
                UploadedByName = string.IsNullOrWhiteSpace(p.UploadedByName) ? (p.UploadedBy?.ToString() ?? "Sistema") : p.UploadedByName,
                Status = p.Status,
                OcrStatus = string.IsNullOrWhiteSpace(p.OcrStatus) ? "NONE" : p.OcrStatus.Trim().ToUpperInvariant(),
                HasOcrText = p.HasOcrText,
                IsOcrAvailable = p.IsOcrAvailable,
                OcrLabel = GetPartOcrLabel(p.OcrStatus, p.HasOcrText),
                OcrCss = GetPartOcrCss(p.OcrStatus, p.HasOcrText),
                OcrUrl = Url.Action(nameof(PartOcrText), "Ged", new { versionId = p.VersionId, partNumber = p.PartNumber, partialGroupId = p.PartialGroupId }) ?? string.Empty,
                PreviewUrl = Url.Action(nameof(Preview), "Ged", new { id = p.VersionId, documentPart = true, partNumber = p.PartNumber, partialGroupId = p.PartialGroupId }) ?? string.Empty,
                DownloadUrl = Url.Action(nameof(Download), "Ged", new { id = p.VersionId, documentPart = true, partNumber = p.PartNumber, partialGroupId = p.PartialGroupId }) ?? string.Empty
            }).ToList(),
            History = history
        };

        await WriteGedAuditAsync("DOCUMENT_PANEL_VIEW", "DOCUMENT", id, "Painel lateral do documento aberto no GED", new { documentId = id, versionId = current.Id, correlationId = HttpContext.TraceIdentifier }, ct);
        return PartialView("_DocumentSidePanel", vm);
    }

    [HttpGet("/Ged/DocumentOcrText")]
    public async Task<IActionResult> DocumentOcrText(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        var status = await ResolveOcrStatusAsync(tenantId, versionId, ct);
        var text = await TryReadOcrTextAsync(tenantId, v.DocumentId, versionId, ct);
        if (!string.IsNullOrWhiteSpace(text) && !string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            status = "COMPLETED";
        }

        await WriteGedAuditAsync("OCR_VIEW", "DOCUMENT_OCR", v.DocumentId, "OCR aberto no painel lateral GED", new { versionId, v.DocumentId, correlationId = HttpContext.TraceIdentifier }, ct);

        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(text))
            return Ok(new { success = true, text, status = "COMPLETED" });

        return Ok(new { success = false, status, message = "OCR ainda não disponível para este documento." });
    }

    [HttpGet("/Ged/PartOcrText")]
    public async Task<IActionResult> PartOcrText(Guid versionId, int? partNumber = null, Guid? partialGroupId = null, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        var status = (await ResolveOcrStatusAsync(tenantId, versionId, ct)).Trim().ToUpperInvariant();
        var text = await TryReadOcrTextAsync(tenantId, v.DocumentId, versionId, ct);
        if (!string.IsNullOrWhiteSpace(text) && !string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            status = "COMPLETED";
        }

        await WriteGedAuditAsync("DOCUMENT_PART_OCR_VIEW", "DOCUMENT_PART", v.DocumentId, "OCR de parte do documento visualizado", new { documentId = v.DocumentId, versionId, partialGroupId, partNumber, tenantId, userId = _currentUser.UserId, correlationId = HttpContext.TraceIdentifier, timestampUtc = DateTime.UtcNow }, ct);

        if (!string.IsNullOrWhiteSpace(text))
        {
            return Ok(new { success = true, status = "COMPLETED", text, message = $"Você está vendo o OCR da Parte {partNumber?.ToString() ?? "selecionada"}." });
        }

        var message = status switch
        {
            "COMPLETED" => "OCR concluído nesta parte, mas nenhum texto foi encontrado.",
            "PENDING" or "QUEUED" => "OCR pendente nesta parte.",
            "PROCESSING" or "RUNNING" => "OCR em processamento nesta parte.",
            "ERROR" or "FAILED" => "OCR com erro nesta parte. Tente reprocessar.",
            _ => "OCR não executado nesta parte."
        };

        return Ok(new { success = false, status = string.IsNullOrWhiteSpace(status) ? "NONE" : status, text = string.Empty, message });
    }

    [HttpGet("/Ged/DocumentHistory")]
    public async Task<IActionResult> DocumentHistory(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var rows = await LoadDocumentHistoryAsync(_currentUser.TenantId, id, 20, ct);
        await WriteGedAuditAsync("DOCUMENT_HISTORY_VIEW", "DOCUMENT_HISTORY", id, "Histórico do documento aberto no painel lateral GED", new { documentId = id, correlationId = HttpContext.TraceIdentifier }, ct);
        return Ok(new { items = rows, hasMore = rows.Count >= 20 });
    }


    [HttpGet("/Ged/DocumentDetailsJson")]
    public async Task<IActionResult> DocumentDetailsJson(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var doc = await _docs.GetAsync(tenantId, id, ct);
        if (doc is null) return NotFound();

        var versions = await _docs.ListVersionsAsync(tenantId, id, ct);
        var current = versions.FirstOrDefault(v => v.Id == doc.CurrentVersionId) ?? versions.FirstOrDefault();
        if (current is null) return NotFound();

        var folderName = await ResolveFolderNameAsync(tenantId, doc.FolderId, ct);
        var typeName = await ResolveDocumentTypeNameAsync(tenantId, doc.TypeId, ct);
        var ocrText = current.IsOcrAvailable ? await TryReadOcrTextAsync(tenantId, id, current.Id, ct) : string.Empty;
        var isOcrAvailable = string.Equals(current.OcrStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            && current.HasOcrText
            && !string.IsNullOrWhiteSpace(ocrText);

        await WriteGedAuditAsync("VIEW", "DOCUMENT", id, "Documento visualizado no painel lateral", new { documentId = id, versionId = current.Id }, ct);

        return Ok(new
        {
            documentId = id,
            versionId = current.Id,
            title = doc.Title,
            fileName = current.FileName,
            typeName,
            folderName,
            uploadAt = current.UploadedAtUtc == default ? current.CreatedAt : current.UploadedAtUtc,
            uploadAtLabel = _dateTimeDisplay.FormatUploadDate(current.UploadedAtUtc == default ? current.CreatedAt : current.UploadedAtUtc),
            createdBy = current.CreatedBy,
            sizeBytes = current.SizeBytes,
            extension = Path.GetExtension(current.FileName)?.Trim('.').ToUpperInvariant(),
            ocrStatus = string.IsNullOrWhiteSpace(current.OcrStatus) ? "NONE" : current.OcrStatus,
            isOcrAvailable,
            ocrText = isOcrAvailable ? ocrText : string.Empty,
            isPartialDocument = current.IsPartialDocument,
            isDocumentIncomplete = current.IsDocumentIncomplete,
            partialStatus = current.PartialStatus,
            partNumber = current.PartNumber ?? current.PartialPartNumber,
            totalParts = current.TotalParts ?? current.PartialTotalParts,
            previewUrl = Url.Action(nameof(Preview), "Ged", new { id = current.Id }),
            ocrUrl = Url.Action(nameof(OcrText), "Ged", new { versionId = current.Id }),
            historyUrl = Url.Action(nameof(DocumentHistoryJson), "Ged", new { id }),
            downloadUrl = Url.Action(nameof(Download), "Ged", new { id = current.Id }),
            versions = versions.Select(v => new
            {
                versionId = v.Id,
                v.FileName,
                v.VersionNumber,
                isCurrent = v.Id == current.Id,
                uploadedAt = v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc,
                uploadedAtLabel = _dateTimeDisplay.FormatUploadDate(v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc),
                v.SizeBytes,
                v.OcrStatus,
                v.IsOcrAvailable,
                v.IsPartialDocument,
                v.IsDocumentIncomplete,
                v.PartNumber,
                v.TotalParts,
                v.PartialStatus
            })
        });
    }


    [HttpGet("/Ged/DocumentParts")]
    [HttpGet("/Ged/DocumentPartsJson")]
    public async Task<IActionResult> DocumentPartsJson(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanViewDocumentParts()) return Forbid();

        var parts = await _documentPartialService.GetPartsAsync(_currentUser.TenantId, id, ct);
        var partialGroupId = parts.Select(p => (Guid?)p.PartialGroupId).FirstOrDefault(x => x.HasValue);
        var summary = partialGroupId.HasValue
            ? await _documentPartialService.GetPartialOcrSummaryAsync(_currentUser.TenantId, partialGroupId.Value, ct)
            : new PartialOcrSummaryDto { TotalParts = parts.Count, PartsWithoutOcr = parts.Count, SummaryText = BuildPartialOcrSummaryText(parts.Count, 0), SummaryCss = "bg-secondary" };
        var total = parts.Select(p => p.TotalParts).Where(x => x.HasValue).DefaultIfEmpty(parts.Count).Max() ?? parts.Count;
        var withOcr = summary.PartsWithOcr;
        var doc = await _docs.GetAsync(_currentUser.TenantId, id, ct);
        await WriteGedAuditAsync("DOCUMENT_PART_TAB_OPEN", "DOCUMENT_PART", id, "Partes do documento visualizadas", new { documentId = id, partialGroupId, userId = _currentUser.UserId, correlationId = HttpContext.TraceIdentifier }, ct);
        return Ok(new
        {
            documentId = id,
            title = doc?.Title ?? string.Empty,
            totalParts = total,
            partsCount = parts.Count,
            partsWithOcr = withOcr,
            partsWithoutOcr = Math.Max(parts.Count - withOcr, 0),
            ocrSummary = summary.SummaryText,
            ocrSummaryCss = summary.SummaryCss,
            partsCompletedWithoutText = summary.PartsCompletedWithoutText,
            parts = parts.Select(p => new
            {
                p.Id,
                p.VersionId,
                p.PartialGroupId,
                p.PartNumber,
                p.TotalParts,
                p.FileName,
                p.SizeBytes,
                uploadedAt = p.UploadedAtUtc,
                uploadedAtLabel = _dateTimeDisplay.FormatUploadDate(p.UploadedAtUtc),
                uploadedBy = p.UploadedBy,
                uploadedByName = p.UploadedByName,
                p.Status,
                p.Notes,
                p.OcrStatus,
                p.HasOcrText,
                p.IsOcrAvailable,
                ocrLabel = GetPartOcrLabel(p.OcrStatus, p.HasOcrText),
                ocrCss = GetPartOcrCss(p.OcrStatus, p.HasOcrText),
                previewUrl = Url.Action(nameof(Preview), "Ged", new { id = p.VersionId, documentPart = true, partNumber = p.PartNumber, partialGroupId = p.PartialGroupId }),
                downloadUrl = Url.Action(nameof(Download), "Ged", new { id = p.VersionId, documentPart = true, partNumber = p.PartNumber, partialGroupId = p.PartialGroupId }),
                ocrUrl = Url.Action(nameof(PartOcrText), "Ged", new { versionId = p.VersionId, partNumber = p.PartNumber, partialGroupId = p.PartialGroupId })
            })
        });
    }


    public sealed class DocumentPartFilterAuditRequest
    {
        public string? FilterName { get; set; }
    }

    [HttpPost("/Ged/DocumentParts/FilterAudit")]
    public async Task<IActionResult> DocumentPartFilterAudit([FromBody] DocumentPartFilterAuditRequest? request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var filterName = string.IsNullOrWhiteSpace(request?.FilterName) ? "unknown" : request.FilterName.Trim();
        await WriteGedAuditAsync("DOCUMENT_PART_FILTER_APPLIED", "DOCUMENT_PART", null, "Filtro rápido de documentos fracionados aplicado", new { filterName, userId = _currentUser.UserId, tenantId = _currentUser.TenantId, correlationId = HttpContext.TraceIdentifier, timestampUtc = DateTime.UtcNow }, ct);
        return Ok(new { success = true });
    }

    [HttpPost("/Ged/DocumentParts/Consolidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsolidateDocumentPart([FromForm] Guid documentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanConsolidateDocumentParts()) return Forbid();
        var result = await _documentPartialService.ConsolidateAsync(_currentUser.TenantId, _currentUser.UserId, documentId, HttpContext.TraceIdentifier, ct);
        if (!result.Success) return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível consolidar o documento." });
        return Ok(new { success = true, message = "Documento consolidado logicamente. As partes originais foram preservadas para auditoria.", summary = result.Value });
    }

    [HttpPost("/Ged/DocumentParts/Complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDocumentPartComplete([FromForm] Guid documentId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanConsolidateDocumentParts()) return Forbid();
        var result = await _documentPartialService.MarkAsCompleteAsync(_currentUser.TenantId, _currentUser.UserId, documentId, HttpContext.TraceIdentifier, ct);
        if (!result.Success) return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível marcar como completo." });
        return Ok(new { success = true, message = "Partes marcadas como completas.", summary = result.Value });
    }

    [HttpPost("/Ged/DocumentParts/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelDocumentPart([FromForm] Guid documentId, [FromForm] string? reason, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanCancelDocumentParts()) return Forbid();
        var result = await _documentPartialService.CancelPartialAsync(_currentUser.TenantId, _currentUser.UserId, documentId, reason, HttpContext.TraceIdentifier, ct);
        if (!result.Success) return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível cancelar o fracionamento." });
        return Ok(new { success = true, message = "Fracionamento cancelado sem apagar arquivos.", summary = result.Value });
    }

    [HttpGet("/Ged/DocumentHistoryJson")]
    public async Task<IActionResult> DocumentHistoryJson(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;
        var versions = await _docs.ListVersionsAsync(tenantId, id, ct);
        await WriteGedAuditAsync("VIEW", "DOCUMENT_HISTORY", id, "Histórico do documento aberto no GED", new { documentId = id }, ct);
        return Ok(new
        {
            items = versions.Select(v => new
            {
                eventType = v.IsCurrent ? "Versão atual" : "Upload de versão",
                fileName = v.FileName,
                occurredAt = v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc,
                occurredAtLabel = _dateTimeDisplay.FormatUploadDate(v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc),
                userId = v.CreatedBy,
                status = v.OcrStatus
            })
        });
    }

    private string ResolveCurrentFolderName(IReadOnlyList<FolderNodeDto> tree, Guid? effectiveFolderId, Guid? requestedFolderId)
    {
        if (!effectiveFolderId.HasValue) return "Pasta selecionada";
        var folder = tree.FirstOrDefault(x => x.Id == effectiveFolderId.Value);
        if (folder is not null) return folder.Name;
        _logger.LogWarning("FolderId informado não foi encontrado na árvore GED; evitando fallback silencioso para Documentos gerais. RequestedFolderId={RequestedFolderId} EffectiveFolderId={EffectiveFolderId}", requestedFolderId, effectiveFolderId);
        return "Pasta selecionada";
    }

    public sealed class CheckDuplicateNamesRequest
    {
        public Guid? FolderId { get; set; }
        public Guid? UploadFolderId { get; set; }
        public Guid? RequestedFolderId { get; set; }
        public IReadOnlyList<string>? FileNames { get; set; }
    }

    [HttpPost("/Ged/Documents/CheckDuplicateNames")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckDuplicateNames([FromBody] CheckDuplicateNamesRequest request, CancellationToken ct)
    {
        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            if (!_currentUser.IsAuthenticated) return Unauthorized(JsonError("Sua sessão expirou. Faça login novamente.", "Autenticação", "Usuário não autenticado.", false, correlationId));
            var isAdmin = await _accessPolicy.IsAdminAsync(_currentUser.TenantId, _currentUser.UserId, User, ct);
            var folderResolution = await ResolveUploadFolderAsync(request?.UploadFolderId ?? request?.FolderId, request?.RequestedFolderId, isAdmin, correlationId, ct);
            if (!folderResolution.Success) return BadRequest(FolderResolutionJsonError(folderResolution, correlationId));
            if (!isAdmin)
            {
                var allowed = await _accessPolicy.CanUploadDocumentToFolderAsync(_currentUser.TenantId, _currentUser.UserId, folderResolution.ResolvedFolderId, User, ct);
                if (!allowed) return StatusCode(403, JsonError("Você não possui permissão para adicionar documentos nesta pasta.", "Autorização", "Permissão negada para upload na pasta.", false, correlationId));
            }
            var fileNames = (request?.FileNames ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (fileNames.Length == 0)
            {
                var emptyDuplicates = Array.Empty<object>();
                return Ok(new { success = true, message = "Nenhum arquivo para validação de duplicidade.", duplicates = emptyDuplicates, requestedFolderId = folderResolution.RequestedFolderId, folderId = folderResolution.ResolvedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, folderName = folderResolution.FolderName, wasVirtual = folderResolution.WasVirtual, createdRealFolder = folderResolution.CreatedRealFolder, data = new { duplicates = emptyDuplicates, requestedFolderId = folderResolution.RequestedFolderId, folderId = folderResolution.ResolvedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, folderName = folderResolution.FolderName, wasVirtual = folderResolution.WasVirtual, createdRealFolder = folderResolution.CreatedRealFolder }, correlationId });
            }

            const string sql = @"select id as ExistingDocumentId, title as FileName from ged.document where tenant_id=@tenantId::uuid and folder_id=@folderId::uuid and title = any(@titles) and reg_status='A';";
            using var con = _db.CreateConnection();
            var rows = await con.QueryAsync(sql, new { tenantId = _currentUser.TenantId, folderId = folderResolution.ResolvedFolderId, titles = fileNames });
            _logger.LogInformation("Duplicidades verificadas. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} ResolvedFolderId={ResolvedFolderId} WasVirtual={WasVirtual} CreatedRealFolder={CreatedRealFolder} FileCount={FileCount} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, folderResolution.RequestedFolderId, folderResolution.ResolvedFolderId, folderResolution.WasVirtual, folderResolution.CreatedRealFolder, fileNames.Length, correlationId);
            return Ok(new { success = true, message = "Duplicidades verificadas com sucesso.", duplicates = rows, requestedFolderId = folderResolution.RequestedFolderId, folderId = folderResolution.ResolvedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, folderName = folderResolution.FolderName, wasVirtual = folderResolution.WasVirtual, createdRealFolder = folderResolution.CreatedRealFolder, data = new { duplicates = rows, requestedFolderId = folderResolution.RequestedFolderId, folderId = folderResolution.ResolvedFolderId, resolvedFolderId = folderResolution.ResolvedFolderId, folderName = folderResolution.FolderName, wasVirtual = folderResolution.WasVirtual, createdRealFolder = folderResolution.CreatedRealFolder }, correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao verificar duplicidade de nomes.");
            return StatusCode(500, JsonError("Não foi possível verificar duplicidades. Tente novamente.", "Servidor", ex.Message, true, HttpContext.TraceIdentifier));
        }
    }

    private async Task<UploadFolderResolutionResult> ResolveUploadFolderAsync(Guid? folderId, Guid? requestedFolderId, bool isAdmin, string correlationId, CancellationToken ct)
    {
        var receivedFolderId = folderId ?? Guid.Empty;
        var requestedId = requestedFolderId ?? receivedFolderId;
        var resolution = await _uploadFolderResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, receivedFolderId, isAdmin, ct);
        resolution.RequestedFolderId = requestedId;
        _logger.LogInformation("Upload/drop destino resolvido. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} ReceivedFolderId={ReceivedFolderId} ResolvedFolderId={ResolvedFolderId} WasVirtual={WasVirtual} CreatedRealFolder={CreatedRealFolder} Success={Success} CanReceiveDocuments={CanReceiveDocuments} FolderName={FolderName} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, resolution.RequestedFolderId, receivedFolderId, resolution.ResolvedFolderId, resolution.WasVirtual, resolution.CreatedRealFolder, resolution.Success, resolution.CanReceiveDocuments, resolution.FolderName, correlationId);
        return resolution;
    }

    private static object FolderResolutionJsonError(UploadFolderResolutionResult resolution, string correlationId)
        => new { success = false, message = resolution.Message, errorStep = "Resolução da pasta", errorLog = resolution.Message, canRetry = false, requestedFolderId = resolution.RequestedFolderId, folderId = resolution.ResolvedFolderId, resolvedFolderId = resolution.ResolvedFolderId, folderName = resolution.FolderName, wasVirtual = resolution.WasVirtual, createdRealFolder = resolution.CreatedRealFolder, correlationId };

    private bool IsAjaxRequest()
        => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
           || (Request.Headers.TryGetValue("Accept", out var a) && a.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        var isAdmin = User.IsInRole(AppRoles.Admin);
        var isOphir = User.IsInRole(AppRoles.AdministradorOphir) || User.IsInRole(AppRoles.ArquivistaOphir);
        if (!isAdmin && isOphir)
        {
            _logger.LogWarning("Acesso bloqueado ao GED para perfil Ophir. Path={Path} User={User}", HttpContext.Request.Path.Value, User.Identity?.Name ?? "anonymous");
            context.Result = Forbid();
        }
    }

    // =========================
    // EXPLORER
    // =========================
    [HttpGet]
    public async Task<IActionResult> Index(Guid? folderId, string? q, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            var tenantId = _currentUser.TenantId;

            var tree = await _folders.TreeAsync(tenantId, ct);
            var isAdmin = await _accessPolicy.IsAdminAsync(tenantId, _currentUser.UserId, User, ct);
            var resolved = await _folderNavigationResolver.ResolveForListingAsync(tenantId, _currentUser.UserId, folderId, isAdmin, ct);

            var effectiveFolderId = resolved.Success ? resolved.VisualFolderId : folderId;
            var listingFolderId = resolved.Success ? resolved.ListingFolderId : (Guid?)null;
            var uploadFolderId = resolved.Success ? resolved.UploadFolderId : (Guid?)null;
            var canUseCurrentFolderForUpload = uploadFolderId.HasValue && uploadFolderId.Value != Guid.Empty;

            var docs = listingFolderId.HasValue
                ? await _docs.ListAsync(tenantId, listingFolderId.Value, q, ct)
                : Array.Empty<DocumentRowDto>();
            const int defaultPageSize = 50;
            var totalDocuments = docs.Count;
            var pagedDocs = docs.Take(defaultPageSize).ToList();

            _logger.LogInformation(
                "GED Index resolvido. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} VisualFolderId={VisualFolderId} ListingFolderId={ListingFolderId} FolderName={FolderName}",
                tenantId, _currentUser.UserId, folderId, effectiveFolderId, listingFolderId, resolved.FolderName);

            // ✅ KPI: documentos não classificados (filtra pela pasta real de listagem quando houver)
            ViewBag.UnclassifiedCount = await _clsQ.CountUnclassifiedAsync(tenantId, listingFolderId, ct);
            ViewBag.RunningOcrCount = await CountRunningOcrJobsAsync(tenantId, ct);

            var vm = new GedExplorerVM
            {
                CanBulkUpload = canUseCurrentFolderForUpload && uploadFolderId.HasValue && await _accessPolicy.CanUploadDocumentToFolderAsync(tenantId, _currentUser.UserId, uploadFolderId, User, ct),
                CurrentFolderId = effectiveFolderId,
                CurrentListingFolderId = listingFolderId,
                CurrentUploadFolderId = uploadFolderId,
                CurrentFolderIsVirtual = resolved.WasVirtual,
                CurrentFolderName = resolved.Success ? resolved.FolderName : ResolveCurrentFolderName(tree, effectiveFolderId, folderId),
                FolderId = listingFolderId,
                Query = q,
                Folders = tree.Select(x => new GedExplorerVM.FolderNodeVM
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name ?? string.Empty,
                    Path = x.Path,
                    Level = x.Level,
                    UploadFolderId = x.UploadFolderId == Guid.Empty ? x.Id : x.UploadFolderId,
                    ListingFolderId = x.ListingFolderId == Guid.Empty ? (x.UploadFolderId == Guid.Empty ? x.Id : x.UploadFolderId) : x.ListingFolderId,
                    IsVirtual = x.IsVirtual,
                    CanReceiveDocuments = x.CanReceiveDocuments && (x.UploadFolderId != Guid.Empty || x.Id != Guid.Empty)
                }).ToList(),
                Page = 1,
                PageSize = defaultPageSize,
                TotalDocuments = totalDocuments,
                HasMoreDocuments = totalDocuments > defaultPageSize,
                Documents = pagedDocs.Select(d => new GedExplorerVM.DocumentRowVM
                {
                    Id = d.Id,
                    Title = d.Title,
                    TypeName = d.TypeName,
                    FileName = d.FileName,
                    CurrentVersionId = d.CurrentVersionId,
                    SizeBytes = d.SizeBytes,
                    CreatedAt = d.CreatedAt,
                    UploadedAtUtc = d.UploadedAtUtc == default ? d.CreatedAt : d.UploadedAtUtc,
                    UploadedAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(d.UploadedAtUtc == default ? d.CreatedAt : d.UploadedAtUtc),
                    CreatedBy = d.CreatedBy,
                    OcrStatus = d.OcrStatus,
                    OcrFinishedAt = d.OcrFinishedAt,
                    HasOcrText = d.HasOcrText,
                    IsOcrAvailable = d.IsOcrAvailable,
                    OcrBadgeText = d.OcrBadgeText,
                    OcrBadgeCss = d.OcrBadgeCss,
                    IsPartialDocument = d.IsPartialDocument,
                    IsDocumentIncomplete = d.IsDocumentIncomplete,
                    PartNumber = d.PartNumber,
                    TotalParts = d.TotalParts,
                    ConsolidatedVersionId = d.ConsolidatedVersionId,
                    PartialStatus = d.PartialStatus,
                    PartialGroupId = d.PartialGroupId,
                    PartialPartNumber = d.PartialPartNumber,
                    PartialTotalParts = d.PartialTotalParts,
                    PartialPartsCount = d.PartialPartsCount,
                    PartialStatusLabel = d.PartialStatusLabel,
                    PartialStatusCss = d.PartialStatusCss,
                    IsConfidential = d.IsConfidential
                }).ToList()
            };

            return View(vm);
        }
        catch (PostgresException ex) when (ex.SqlState == "42703")
        {
            _logger.LogError(ex, "Erro de schema ao carregar Explorer GED. FolderId={FolderId} q={Q}", folderId, q);

            TempData["Error"] = "A estrutura do banco de dados está desatualizada. Execute as migrations do sistema.";

            return View(new GedExplorerVM
            {
                ErrorMessage = "Estrutura do banco desatualizada. Campo de data de upload não encontrado."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar Explorer. FolderId={FolderId} q={q}", folderId, q);
            TempData["Error"] = "Erro ao carregar Explorer.";
            return View(new GedExplorerVM { ErrorMessage = "Erro ao carregar Explorer." });
        }
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpGet]
    public async Task<IActionResult> Processing(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return RedirectToAction("Login", "Account");

        var tenantId = _currentUser.TenantId;
        var rows = await ListRunningOcrJobsAsync(tenantId, ct);
        ViewBag.ProcessingMetrics = await GetProcessingMetricsAsync(tenantId, ct);
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Kpi(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return RedirectToAction("Login", "Account");

        var tenantId = _currentUser.TenantId;

        const string sql = @"
SELECT
  (SELECT count(*)::int FROM ged.document d WHERE d.tenant_id = @tenantId AND d.reg_status = 'A') AS TotalDocuments,
  (SELECT count(*)::int FROM ged.document d WHERE d.tenant_id = @tenantId AND d.reg_status = 'A' AND d.created_at >= date_trunc('month', now())) AS DocumentsThisMonth,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'PROCESSING'::ged.ocr_status_enum) AS OcrProcessing,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'PENDING'::ged.ocr_status_enum) AS OcrPending,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'COMPLETED'::ged.ocr_status_enum AND j.finished_at >= now() - interval '24 hours') AS OcrCompleted24h,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'ERROR'::ged.ocr_status_enum AND j.finished_at >= now() - interval '24 hours') AS OcrErrors24h,
  (SELECT count(*)::int FROM ged.document d WHERE d.tenant_id = @tenantId AND d.reg_status = 'A' AND (d.description IS NULL OR btrim(d.description) = '' OR btrim(d.description) = '-')) AS DocumentsWithoutDescription;";

        using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleAsync(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

        var vm = new GedKpiVm
        {
            TotalDocuments = (int)row.totaldocuments,
            DocumentsThisMonth = (int)row.documentsthismonth,
            OcrProcessing = (int)row.ocrprocessing,
            OcrPending = (int)row.ocrpending,
            OcrCompleted24h = (int)row.ocrcompleted24h,
            OcrErrors24h = (int)row.ocrerrors24h,
            DocumentsWithoutDescription = (int)row.documentswithoutdescription
        };

        return View(vm);
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpGet]
    public async Task<IActionResult> ProcessingStatus(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var rows = await ListRunningOcrJobsAsync(tenantId, ct);
        return Json(new { success = true, count = rows.Count, items = rows });
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpGet]
    public async Task<IActionResult> ProcessingMetrics(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var metrics = await GetProcessingMetricsAsync(tenantId, ct);
        var recentErrors = await ListRecentOcrErrorsAsync(tenantId, ct);

        return Json(new { success = true, metrics, recentErrors });
    }

    [HttpGet]
    public async Task<IActionResult> QueueSnapshot(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;

        const string sql = @"
WITH base AS (
  SELECT
    j.id AS job_id,
    j.document_version_id AS version_id,
    d.id AS document_id,
    d.title AS document_title,
    j.status::text AS status_text,
    j.requested_at,
    j.started_at
  FROM ged.ocr_job j
  JOIN ged.document_version dv
    ON dv.tenant_id = j.tenant_id
   AND dv.id = j.document_version_id
  JOIN ged.document d
    ON d.tenant_id = dv.tenant_id
   AND d.id = dv.document_id
  WHERE j.tenant_id = @tenantId
    AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
),
totals AS (
  SELECT
    count(*) FILTER (WHERE status_text = 'PENDING')::int AS pending_count,
    count(*) FILTER (WHERE status_text = 'PROCESSING')::int AS processing_count
  FROM base
)
SELECT
  b.job_id AS JobId,
  b.version_id AS VersionId,
  b.document_id AS DocumentId,
  b.document_title AS DocumentTitle,
  b.status_text AS Status,
  b.requested_at AS RequestedAt,
  b.started_at AS StartedAt,
  row_number() OVER (ORDER BY b.requested_at) AS QueuePosition,
  t.pending_count AS PendingCount,
  t.processing_count AS ProcessingCount
FROM base b
CROSS JOIN totals t
ORDER BY b.requested_at
LIMIT 50;";

        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<QueueSnapshotRowVm>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).ToList();

        var pendingCount = rows.FirstOrDefault()?.PendingCount ?? 0;
        var processingCount = rows.FirstOrDefault()?.ProcessingCount ?? 0;

        return Json(new
        {
            success = true,
            pendingCount,
            processingCount,
            total = pendingCount + processingCount,
            items = rows
        });
    }

    private async Task<int> CountRunningOcrJobsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT count(1)
FROM ged.ocr_job j
WHERE j.tenant_id = @tenantId
  AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum);";

        using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
    }

    private async Task<List<ProcessingRowVm>> ListRunningOcrJobsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  j.id AS JobId,
  j.document_version_id AS VersionId,
  d.id AS DocumentId,
  d.title AS DocumentTitle,
  j.status::text AS Status,
  j.requested_at AS RequestedAt,
  j.started_at AS StartedAt
FROM ged.ocr_job j
JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
JOIN ged.document d
  ON d.tenant_id = dv.tenant_id
 AND d.id = dv.document_id
WHERE j.tenant_id = @tenantId
  AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
ORDER BY j.requested_at DESC
LIMIT 200;";

        using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessingRowVm>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<ProcessingMetricsVm> GetProcessingMetricsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  count(*) FILTER (WHERE j.status = 'PENDING'::ged.ocr_status_enum)    AS PendingCount,
  count(*) FILTER (WHERE j.status = 'PROCESSING'::ged.ocr_status_enum) AS ProcessingCount,
  count(*) FILTER (WHERE j.status = 'ERROR'::ged.ocr_status_enum
                   AND j.requested_at >= now() - interval '24 hours')   AS Errors24h,
  coalesce(avg(extract(epoch from (j.started_at - j.requested_at)))
      FILTER (WHERE j.started_at IS NOT NULL
              AND j.requested_at >= now() - interval '24 hours'), 0)     AS AvgQueueSeconds
FROM ged.ocr_job j
WHERE j.tenant_id = @tenantId;";

        using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleAsync(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

        return new ProcessingMetricsVm
        {
            PendingCount = (int)row.pendingcount,
            ProcessingCount = (int)row.processingcount,
            Errors24h = (int)row.errors24h,
            AvgQueueSeconds = Convert.ToDecimal(row.avgqueueseconds)
        };
    }

    private async Task<List<ProcessingErrorVm>> ListRecentOcrErrorsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  j.id AS JobId,
  d.id AS DocumentId,
  d.title AS DocumentTitle,
  j.error_message AS ErrorMessage,
  j.finished_at AS FinishedAt
FROM ged.ocr_job j
JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
JOIN ged.document d
  ON d.tenant_id = dv.tenant_id
 AND d.id = dv.document_id
WHERE j.tenant_id = @tenantId
  AND j.status = 'ERROR'::ged.ocr_status_enum
ORDER BY j.finished_at DESC NULLS LAST
LIMIT 20;";

        using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessingErrorVm>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    public sealed class ProcessingRowVm
    {
        public long JobId { get; set; }
        public Guid VersionId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime RequestedAt { get; set; }
        public DateTime? StartedAt { get; set; }
    }

    public sealed class ProcessingMetricsVm
    {
        public int PendingCount { get; set; }
        public int ProcessingCount { get; set; }
        public int Errors24h { get; set; }
        public decimal AvgQueueSeconds { get; set; }
    }

    public sealed class ProcessingErrorVm
    {
        public long JobId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    public sealed class QueueSnapshotRowVm
    {
        public long JobId { get; set; }
        public Guid VersionId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime RequestedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public int QueuePosition { get; set; }
        public int PendingCount { get; set; }
        public int ProcessingCount { get; set; }
    }

    public sealed class GedKpiVm
    {
        public int TotalDocuments { get; set; }
        public int DocumentsThisMonth { get; set; }
        public int OcrProcessing { get; set; }
        public int OcrPending { get; set; }
        public int OcrCompleted24h { get; set; }
        public int OcrErrors24h { get; set; }
        public int DocumentsWithoutDescription { get; set; }
    }

    // =========================
    // DETAILS
    // =========================
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, Guid? versionId, bool? openClassify, bool? ocrSwitched, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            var tenantId = _currentUser.TenantId;

            var doc = await _docs.GetAsync(tenantId, id, ct);
            if (doc is null) return NotFound();

            var versions = await _docs.ListVersionsAsync(tenantId, id, ct);

            // ========= WORKFLOW =========
            var wfState = await _docWfQ.GetCurrentAsync(tenantId, id, ct);

            var wfVm = new GedDetailsVM.WorkflowVM
            {
                HasActiveWorkflow = wfState is not null,
                DocumentWorkflowId = wfState?.DocumentWorkflowId,
                WorkflowId = wfState?.WorkflowId,
                WorkflowName = wfState?.WorkflowName,
                CurrentStageId = wfState?.CurrentStageId,
                CurrentStageName = wfState?.CurrentStageName,
                IsCompleted = wfState?.IsCompleted ?? false,
                StartedAt = wfState?.StartedAt,
                StartedBy = wfState?.StartedBy
            };

            var defs = await _workflowQ.ListDefinitionsAsync(tenantId, q: null, ct);
            wfVm.AvailableWorkflows = defs
                .Where(x => x.IsActive)
                .Select(x => new GedDetailsVM.WorkflowVM.WorkflowDefinitionRow
                {
                    Id = x.Id,
                    Name = $"{x.Code} - {x.Name}"
                })
                .ToList();

            if (wfState is not null && !wfState.IsCompleted)
            {
                var transitions = await _docWfQ.ListAvailableTransitionsAsync(tenantId, id, ct);
                wfVm.AvailableTransitions = transitions.Select(t => new GedDetailsVM.WorkflowVM.TransitionRow
                {
                    Id = t.Id,
                    Name = t.Name,
                    ToStageId = t.ToStageId,
                    ToStageName = t.ToStageName,
                    RequiresReason = t.RequiresReason
                }).ToList();
            }

            var history = await _docWfQ.ListHistoryAsync(tenantId, id, ct);
            wfVm.History = history.Select(h => new GedDetailsVM.WorkflowVM.HistoryRow
            {
                Id = h.Id,
                FromStageName = h.FromStageName,
                ToStageName = h.ToStageName,
                PerformedAt = h.PerformedAt,
                PerformedBy = h.PerformedBy,
                Reason = h.Reason,
                Comments = h.Comments
            }).ToList();

            var vm = new GedDetailsVM
            {
                Id = doc.Id,
                FolderId = doc.FolderId,
                Title = doc.Title,
                Description = doc.Description,
                IsConfidential = doc.IsConfidential,
                CreatedBy = doc.CreatedBy,
                CurrentVersionId = doc.CurrentVersionId,
                Versions = versions.Select(v => new GedDetailsVM.VersionVM
                {
                    Id = v.Id,
                    VersionNumber = v.VersionNumber,
                    FileName = v.FileName,
                    ContentType = v.ContentType ?? "",
                    SizeBytes = v.SizeBytes,
                    CreatedAt = v.CreatedAt,
                    UploadedAtUtc = v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc,
                    UploadedAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc),
                    CreatedBy = v.CreatedBy,
                    IsCurrent = doc.CurrentVersionId.HasValue && v.Id == doc.CurrentVersionId.Value,
                    HasOcrText = v.HasOcrText,
                    IsOcrAvailable = v.IsOcrAvailable,
                    IsPartialDocument = v.IsPartialDocument,
                    IsDocumentIncomplete = v.IsDocumentIncomplete,
                    PartialGroupId = v.PartialGroupId,
                    PartialPartNumber = v.PartialPartNumber,
                    PartialTotalParts = v.PartialTotalParts,
                    PartialStatus = v.PartialStatus,
                    PartNumber = v.PartNumber,
                    TotalParts = v.TotalParts,
                    ConsolidatedVersionId = v.ConsolidatedVersionId,

                    // OCR (se existir no DTO)
                    OcrStatus = v.OcrStatus,
                    OcrJobId = v.OcrJobId,
                    OcrErrorMessage = v.OcrErrorMessage,
                    OcrRequestedAt = v.OcrRequestedAt,
                    OcrStartedAt = v.OcrStartedAt,
                    OcrFinishedAt = v.OcrFinishedAt,
                    OcrInvalidateDigitalSignatures = v.OcrInvalidateDigitalSignatures
                }).ToList(),
                Workflow = wfVm
            };

            vm.SelectedVersionId =
                versionId
                ?? vm.CurrentVersionId
                ?? vm.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault()?.Id;

            ViewBag.OpenClassify = openClassify == true;
            ViewBag.OcrSwitched = ocrSwitched == true;

            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar Details. DocId={DocId}", id);
            TempData["erro"] = "Erro ao carregar detalhes do documento.";
            return RedirectToAction(nameof(Index));
        }
    }

    // =========================
    // WORKFLOW ACTIONS (TRAVA)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartWorkflow([FromForm] Guid documentId, [FromForm] Guid workflowId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var hasCls = await _clsQ.HasClassificationAsync(tenantId, documentId, ct);
            if (!hasCls)
            {
                TempData["Error"] = "Este documento precisa estar classificado antes de iniciar o workflow.";
                return RedirectToAction(nameof(Details), new { id = documentId, openClassify = true });
            }

            var result = await _docWfC.StartAsync(tenantId, documentId, workflowId, userId, ct);
            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha ao iniciar workflow.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            TempData["Success"] = "Workflow iniciado com sucesso.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro StartWorkflow. DocId={DocId} WorkflowId={WorkflowId}", documentId, workflowId);
            TempData["Error"] = "Erro inesperado ao iniciar workflow.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyTransition(
        [FromForm] Guid documentId,
        [FromForm] Guid transitionId,
        [FromForm] string? reason,
        [FromForm] string? comments,
        CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var hasCls = await _clsQ.HasClassificationAsync(tenantId, documentId, ct);
            if (!hasCls)
            {
                TempData["Error"] = "Documento sem classificação. Classifique antes de avançar no workflow.";
                return RedirectToAction(nameof(Details), new { id = documentId, openClassify = true });
            }

            var result = await _docWfC.ApplyTransitionAsync(
                tenantId,
                documentId,
                transitionId,
                reason,
                comments,
                userId,
                ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha ao aplicar transição.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            TempData["Success"] = "Transição aplicada com sucesso.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ApplyTransition. DocId={DocId} TransitionId={TransitionId}", documentId, transitionId);
            TempData["Error"] = "Erro inesperado ao aplicar transição.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
    }

    // =========================
    // CRIAR PASTA (modal)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder([FromForm] CreateFolderCommand cmd, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var result = await _folderCommands.CreateAsync(
                tenantId,
                cmd.Name,
                cmd.ParentId,
                cmd.DepartmentId,
                userId,
                ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha ao criar pasta.";
                return RedirectToAction(nameof(Index), new { folderId = cmd.ParentId });
            }

            TempData["Success"] = "Pasta criada com sucesso.";
            return RedirectToAction(nameof(Index), new { folderId = result.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar pasta. ParentId={ParentId} Name={Name}", cmd.ParentId, cmd.Name);
            TempData["Error"] = "Erro ao criar pasta.";
            return RedirectToAction(nameof(Index), new { folderId = cmd.ParentId });
        }
    }

    // =========================
    // UPLOAD
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        [FromForm] Guid folderId,
        [FromForm] IFormFile file,
        [FromForm] string? title,
        [FromForm] bool? isConfidential,
        [FromForm] bool? isDocumentPart,
        [FromForm] int? partNumber,
        [FromForm] int? totalParts,
        [FromForm] string? partialNotes,
        CancellationToken ct)
    {
        try
        {
            if (IsAjaxRequest())
            {
                return BadRequest(JsonError("Endpoint legado (/Ged/Upload). Use /Ged/Documents/CheckDuplicateNames e /Ged/Documents/BulkUploadSingle.", "Fluxo legado", "Upload legado bloqueado para chamadas AJAX.", false, HttpContext.TraceIdentifier));
            }

            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            if (file == null || file.Length <= 0)
            {
                TempData["Error"] = "Selecione um arquivo válido.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            if (isDocumentPart == true && !CanAddDocumentPart())
            {
                TempData["Error"] = "Você não possui permissão para marcar documentos como incompletos ou adicionar partes.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();

            await using var stream = file.OpenReadStream();

            var cmd = new UploadDocumentCommand
            {
                FolderId = folderId,
                Title = string.IsNullOrWhiteSpace(title)
                    ? Path.GetFileNameWithoutExtension(file.FileName)
                    : title.Trim(),
                Description = string.IsNullOrWhiteSpace(partialNotes) ? null : partialNotes.Trim(),
                IsConfidential = isConfidential ?? false,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = stream,
                IsPartialDocument = isDocumentPart == true,
                IsDocumentIncomplete = isDocumentPart == true,
                PartNumber = isDocumentPart == true ? partNumber : null,
                TotalParts = isDocumentPart == true ? totalParts : null,
                PartialPartNumber = isDocumentPart == true ? partNumber : null,
                PartialTotalParts = isDocumentPart == true ? totalParts : null,
                PartialStatus = isDocumentPart == true ? "INCOMPLETE" : "NOT_PARTIAL"
            };

            var result = await _documentApp.UploadAsync(cmd, ip, ua, ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha no upload.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            TempData["Success"] = isDocumentPart == true ? "Documento incompleto enviado com sucesso. Ele poderá receber novas partes futuramente." : "Upload realizado com sucesso.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no upload do GED. FolderId={FolderId}", folderId);
            TempData["Error"] = "Erro ao enviar arquivo.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
    }

    [HttpPost("/Ged/DocumentParts/MarkIncomplete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDocumentIncomplete([FromForm] Guid documentId, [FromForm] int? totalParts, [FromForm] string? notes, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (!CanAddDocumentPart()) return Forbid();
        if (documentId == Guid.Empty) return BadRequest(new { success = false, message = "Documento inválido." });

        var result = await _documentPartialService.MarkAsIncompleteAsync(_currentUser.TenantId, _currentUser.UserId, documentId, totalParts, notes, HttpContext.TraceIdentifier, ct);
        if (!result.Success) return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível marcar como Documento incompleto." });
        return Ok(new { success = true, message = "Documento marcado como incompleto. Agora ele poderá receber novas partes futuramente.", summary = result.Value });
    }

    private object JsonSuccess(string message, object? data = null, string? correlationId = null)
        => new { success = true, message, data = data ?? new { }, correlationId = correlationId ?? HttpContext.TraceIdentifier };


    private static GedExplorerVM.DocumentRowVM MapDocumentRow(DocumentRowDto d)
        => new()
        {
            Id = d.Id,
            Title = d.Title,
            TypeName = d.TypeName,
            FileName = d.FileName,
            CurrentVersionId = d.CurrentVersionId,
            SizeBytes = d.SizeBytes,
            CreatedAt = d.CreatedAt,
            UploadedAtUtc = d.UploadedAtUtc == default ? d.CreatedAt : d.UploadedAtUtc,
            CreatedBy = d.CreatedBy,
            OcrStatus = d.OcrStatus,
            OcrFinishedAt = d.OcrFinishedAt,
            HasOcrText = d.HasOcrText,
            IsOcrAvailable = d.IsOcrAvailable,
            OcrBadgeText = d.OcrBadgeText,
            OcrBadgeCss = d.OcrBadgeCss,
            PartialPartsWithOcrCount = d.PartialPartsWithOcrCount,
            PartialPartsWithoutOcrCount = d.PartialPartsWithoutOcrCount,
            HasAnyPartialOcr = d.HasAnyPartialOcr,
            HasAllPartialOcr = d.HasAllPartialOcr,
            OcrSummaryText = d.OcrSummaryText,
            OcrSummaryCss = d.OcrSummaryCss,
            ClassificationId = d.ClassificationId,
            ClassificationLabel = d.ClassificationLabel,
            ClassificationColor = d.ClassificationColor,
            ClassificationIcon = d.ClassificationIcon,
            HasOcrClassificationSuggestion = d.HasOcrClassificationSuggestion,
            SuggestedClassificationId = d.SuggestedClassificationId,
            SuggestedClassificationLabel = d.SuggestedClassificationLabel,
            IsPartialDocument = d.IsPartialDocument,
            IsDocumentIncomplete = d.IsDocumentIncomplete,
            PartNumber = d.PartNumber,
            TotalParts = d.TotalParts,
            ConsolidatedVersionId = d.ConsolidatedVersionId,
            PartialStatus = d.PartialStatus,
            PartialGroupId = d.PartialGroupId,
            PartialPartNumber = d.PartialPartNumber,
            PartialTotalParts = d.PartialTotalParts,
            PartialPartsCount = d.PartialPartsCount,
            PartialStatusLabel = d.PartialStatusLabel,
            PartialStatusCss = d.PartialStatusCss,
            IsConfidential = d.IsConfidential
        };


    private static string FormatBytes(long value)
    {
        if (value <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    private static string ResolveFileExtension(string? fileName, string? fileExtension)
    {
        var extension = !string.IsNullOrWhiteSpace(fileExtension) ? fileExtension : Path.GetExtension(fileName ?? string.Empty);
        extension = (extension ?? string.Empty).Trim().TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? "-" : extension.ToUpperInvariant();
    }

    private static string BuildPartialOcrSummaryText(int totalParts, int partsWithOcr)
    {
        if (totalParts <= 0) return "Sem partes registradas";
        if (partsWithOcr <= 0) return "Sem OCR nas partes";
        if (partsWithOcr >= totalParts) return "OCR disponível nas partes";
        return $"OCR parcial {partsWithOcr}/{totalParts}";
    }

    private static string GetPartOcrLabel(string? status, bool hasText)
    {
        var normalized = (status ?? "NONE").Trim().ToUpperInvariant();
        return normalized switch
        {
            "COMPLETED" => hasText ? "OCR disponível" : "OCR concluído sem texto",
            "PENDING" or "QUEUED" => "OCR pendente",
            "PROCESSING" or "RUNNING" => "OCR em processamento",
            "ERROR" or "FAILED" => "OCR com erro",
            _ => "Sem OCR"
        };
    }

    private static string GetPartOcrCss(string? status, bool hasText)
    {
        var normalized = (status ?? "NONE").Trim().ToUpperInvariant();
        return normalized switch
        {
            "COMPLETED" => hasText ? "bg-success" : "bg-light text-dark border",
            "PENDING" or "QUEUED" => "bg-warning text-dark",
            "PROCESSING" or "RUNNING" => "bg-info text-dark",
            "ERROR" or "FAILED" => "bg-danger",
            _ => "bg-secondary"
        };
    }

    private static string GetOcrBadgeText(string? status, bool isAvailable)
    {
        if (isAvailable) return "OCR disponível";
        return (status ?? "NONE").Trim().ToUpperInvariant() switch
        {
            "NONE" or "" => "Sem OCR",
            "PENDING" or "QUEUED" => "OCR pendente",
            "PROCESSING" or "RUNNING" => "OCR em processamento",
            "ERROR" or "FAILED" => "OCR com erro",
            "COMPLETED" => "OCR concluído sem texto",
            var value => $"OCR: {value}"
        };
    }

    private async Task<string> ResolveUserNameAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty) return "Sistema";
        try
        {
            const string sql = @"
select coalesce(nullif(name,''), nullif(email,''), id::text)
from ged.users
where tenant_id=@tenantId and id=@userId
limit 1";
            await using var con = await _db.OpenAsync(ct);
            return await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct)) ?? userId.Value.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao resolver usuário no painel GED. UserId={UserId}", userId);
            return userId.Value.ToString();
        }
    }

    private async Task<string> ResolveOcrStatusAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        try
        {
            const string sql = @"
select coalesce(status::text, 'NONE')
from ged.ocr_job
where tenant_id=@tenantId and document_version_id=@versionId
order by requested_at desc
limit 1";
            await using var con = await _db.OpenAsync(ct);
            return await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, versionId }, cancellationToken: ct)) ?? "NONE";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao resolver status OCR no painel GED. VersionId={VersionId}", versionId);
            return "NONE";
        }
    }

    private async Task<IReadOnlyList<DocumentSidePanelHistoryVm>> LoadDocumentHistoryAsync(Guid tenantId, Guid documentId, int take, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            const string auditSql = @"
select
  a.created_at as ""OccurredAtUtc"",
  coalesce(a.user_name, u.name, u.email, a.user_id::text, 'Sistema') as ""UserName"",
  coalesce(a.action::text, '-') as ""Action"",
  coalesce(a.message, a.entity_name, '-') as ""Description"",
  coalesce(a.correlation_id, a.details->>'correlationId', a.details->>'CorrelationId') as ""CorrelationId""
from ged.app_audit_log a
left join ged.users u on u.tenant_id = a.tenant_id and u.id = a.user_id
where a.tenant_id=@tenantId
  and (a.entity_id::text=@documentIdText or a.details::text ilike '%' || @documentIdText || '%')
order by a.created_at desc
limit @take";
            var auditRows = (await con.QueryAsync<PanelHistoryRow>(new CommandDefinition(auditSql, new { tenantId, documentIdText = documentId.ToString(), take }, cancellationToken: ct))).AsList();
            if (auditRows.Count > 0)
            {
                return auditRows.Select(x => new DocumentSidePanelHistoryVm
                {
                    OccurredAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(x.OccurredAtUtc),
                    UserName = x.UserName,
                    Action = x.Action,
                    Description = x.Description,
                    CorrelationId = x.CorrelationId
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar app_audit_log do painel GED; usando versões. DocumentId={DocumentId}", documentId);
        }

        var versions = await _docs.ListVersionsAsync(tenantId, documentId, ct);
        return versions.Take(take).Select(v => new DocumentSidePanelHistoryVm
        {
            OccurredAtLocalFormatted = _dateTimeDisplay.FormatUploadDate(v.UploadedAtUtc == default ? v.CreatedAt : v.UploadedAtUtc),
            UserName = v.CreatedBy?.ToString() ?? "Sistema",
            Action = v.IsCurrent ? "Versão atual" : "Upload de versão",
            Description = v.FileName,
            CorrelationId = null
        }).ToList();
    }

    private sealed class PanelHistoryRow
    {
        public DateTime OccurredAtUtc { get; set; }
        public string UserName { get; set; } = "";
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public string? CorrelationId { get; set; }
    }


    private async Task<string> ResolveFolderNameAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        if (!folderId.HasValue) return "Raiz";
        try
        {
            const string sql = "select name from ged.folder where tenant_id=@tenantId and id=@folderId limit 1";
            using var con = _db.CreateConnection();
            return await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct)) ?? "Pasta selecionada";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao resolver nome da pasta no painel GED. FolderId={FolderId}", folderId);
            return "Pasta selecionada";
        }
    }

    private async Task<string> ResolveDocumentTypeNameAsync(Guid tenantId, Guid? typeId, CancellationToken ct)
    {
        if (!typeId.HasValue) return "Sem classificação";
        try
        {
            const string sql = "select name from ged.document_type where tenant_id=@tenantId and id=@typeId limit 1";
            using var con = _db.CreateConnection();
            return await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, typeId }, cancellationToken: ct)) ?? "Sem classificação";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao resolver tipo documental no painel GED. TypeId={TypeId}", typeId);
            return "Sem classificação";
        }
    }

    private async Task<string> TryReadOcrTextAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            const string schemaSql = """
SELECT
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='ocr_text') AS "HasOcrText",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_id') AS "HasDocumentId",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='version_id') AS "HasVersionId",
    EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='ged' AND table_name='document_search' AND column_name='document_version_id') AS "HasDocumentVersionId";
""";
            var schema = await con.QuerySingleAsync<DocumentSearchSchemaInfo>(new CommandDefinition(schemaSql, cancellationToken: ct));
            if (schema.HasOcrText)
            {
                var predicates = new List<string>();
                if (schema.HasDocumentId) predicates.Add("document_id=@documentId");
                var versionPredicates = new List<string>();
                if (schema.HasVersionId) versionPredicates.Add("version_id=@versionId");
                if (schema.HasDocumentVersionId) versionPredicates.Add("document_version_id=@versionId");
                if (versionPredicates.Count > 0) predicates.Add($"({string.Join(" OR ", versionPredicates)})");
                var entityPredicate = predicates.Count == 0 ? "true" : string.Join(" AND ", predicates);
                var sql = $"select ocr_text from ged.document_search where tenant_id=@tenantId and {entityPredicate} and NULLIF(btrim(COALESCE(ocr_text,'')),'') IS NOT NULL limit 1";
                var dbText = await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, documentId, versionId }, cancellationToken: ct));
                if (!string.IsNullOrWhiteSpace(dbText)) return dbText;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler OCR no índice; tentando storage. DocumentId={DocumentId} VersionId={VersionId}", documentId, versionId);
        }

        var ocrRelPath = Path.Combine(tenantId.ToString("N"), "ocr", documentId.ToString("N"), versionId.ToString("N"), "ocr.txt").Replace('\\', '/');
        if (!await _storage.ExistsAsync(ocrRelPath, ct)) return string.Empty;
        await using var stream = await _storage.OpenReadAsync(ocrRelPath, ct);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    private sealed class DocumentSearchSchemaInfo
    {
        public bool HasOcrText { get; set; }
        public bool HasDocumentId { get; set; }
        public bool HasVersionId { get; set; }
        public bool HasDocumentVersionId { get; set; }
    }

    private async Task WriteGedAuditAsync(string action, string entity, Guid? entityId, string summary, object data, CancellationToken ct)
    {
        try
        {
            await _auditWriter.WriteAsync(_currentUser.TenantId, _currentUser.UserId, action, entity, entityId, summary, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), data, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao registrar auditoria GED. Action={Action} Entity={Entity} EntityId={EntityId}", action, entity, entityId);
        }
    }


    private bool CanAddDocumentPart() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Arquivista) || User.IsInRole(AppRoles.ArquivistaOphir);
    private bool CanViewDocumentParts() => _currentUser.IsAuthenticated;
    private bool CanConsolidateDocumentParts() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Arquivista) || User.IsInRole(AppRoles.ArquivistaOphir);
    private bool CanCancelDocumentParts() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.AdministradorOphir);

    private object JsonError(string message, string errorStep, string errorLog, bool canRetry, string? correlationId = null)
        => new { success = false, message, errorStep, errorLog, canRetry, correlationId = correlationId ?? HttpContext.TraceIdentifier };

    // =========================
    // DOWNLOAD (por versionId)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Download(Guid id, bool documentPart = false, int? partNumber = null, Guid? partialGroupId = null, CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, id, ct);
            if (v == null) return NotFound();

            await WriteGedAuditAsync(documentPart ? "DOCUMENT_PART_DOWNLOAD" : "DOCUMENT_DOWNLOAD", documentPart ? "DOCUMENT_PART" : "DOCUMENT_VERSION", id, documentPart ? "Download de parte de documento" : "Download de documento GED", new { versionId = id, v.DocumentId, v.FileName, partialGroupId, partNumber, tenantId = _currentUser.TenantId, userId = _currentUser.UserId, correlationId = HttpContext.TraceIdentifier, timestampUtc = DateTime.UtcNow }, ct);

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
            var contentType = string.IsNullOrWhiteSpace(v.ContentType) ? "application/octet-stream" : v.ContentType;

            return File(stream, contentType, v.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Download. VersionId={VersionId}", id);
            return StatusCode(500);
        }
    }

    // =========================
    // PREVIEW (inline antigo)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Preview(Guid id, bool documentPart = false, int? partNumber = null, Guid? partialGroupId = null, CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, id, ct);
            if (v == null) return NotFound();

            await WriteGedAuditAsync(documentPart ? "DOCUMENT_PART_PREVIEW" : "FILE_PREVIEW", documentPart ? "DOCUMENT_PART" : "DOCUMENT_PREVIEW", id, documentPart ? "Preview de parte de documento" : "Preview de documento GED", new { versionId = id, v.DocumentId, v.FileName, partialGroupId, partNumber, tenantId = _currentUser.TenantId, userId = _currentUser.UserId, correlationId = HttpContext.TraceIdentifier, timestampUtc = DateTime.UtcNow }, ct);

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
            var contentType = string.IsNullOrWhiteSpace(v.ContentType) ? "application/octet-stream" : v.ContentType;

            SetInlineContentDisposition(v.FileName);

            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Preview. VersionId={VersionId}", id);
            return StatusCode(500);
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadVersion(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
            return File(stream, v.ContentType, v.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em DownloadVersion. VersionId={VersionId}", versionId);
            return StatusCode(500);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Folders(CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;
            var tree = await _folders.TreeAsync(tenantId, ct);

            var vm = new FoldersPageVM
            {
                Tree = tree.Select(x => new FoldersPageVM.FolderVM
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name ?? string.Empty,
                    Path = x.Path,
                    Level = x.Level,
                    UploadFolderId = x.UploadFolderId == Guid.Empty ? x.Id : x.UploadFolderId,
                    ListingFolderId = x.ListingFolderId == Guid.Empty ? (x.UploadFolderId == Guid.Empty ? x.Id : x.UploadFolderId) : x.ListingFolderId,
                    IsVirtual = x.IsVirtual,
                    CanReceiveDocuments = x.CanReceiveDocuments && (x.UploadFolderId != Guid.Empty || x.Id != Guid.Empty)
                }).ToList()
            };

            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar tela de pastas.");
            TempData["erro"] = "Erro ao carregar pastas.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPasta(string nome, Guid? parentId, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var result = await _folderCommands.CreateAsync(
                tenantId: tenantId,
                name: nome,
                parentId: parentId,
                departmentId: null,
                createdBy: userId,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta criada com sucesso." : (result.Error?.Message ?? "Falha ao criar pasta.");

            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar pasta.");
            TempData["erro"] = "Erro ao criar pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenomearPasta(Guid id, string nome, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            var result = await _folderCommands.RenameAsync(
                tenantId: tenantId,
                folderId: id,
                newName: nome,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta renomeada com sucesso." : (result.Error?.Message ?? "Falha ao renomear pasta.");

            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao renomear pasta. FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao renomear pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirPasta(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            await _folderCommands.DeactivateAsync(
                tenantId: tenantId,
                folderId: id,
                userId: _currentUser.UserId,
                ct: ct);

            TempData["ok"] = "Pasta excluída com sucesso.";
            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desativar pasta. FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao desativar pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirPastaExplorer(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("=== DELETE FOLDER START === FolderId={Folder}", id);

        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        try
        {
            var r = await _folderCommands.DeleteRecursiveAsync(tenantId, id, userId, ct);
            if (!r.Success)
            {
                _logger.LogWarning("DELETE FOLDER FAIL: {Msg}", r.Error?.Message);
                TempData["erro"] = r.Error?.Message ?? "Erro ao excluir pasta.";
                return RedirectToAction(nameof(Folders));
            }

            TempData["ok"] = "Pasta excluída.";
            _logger.LogInformation("=== DELETE FOLDER SUCCESS === FolderId={FolderId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== DELETE FOLDER ERROR === FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao excluir pasta.";
        }

        return RedirectToAction(nameof(Folders));
    }

    private static bool IsPdf(string? ct, string name)
        => (!string.IsNullOrWhiteSpace(ct) && ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
           || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(string? ct, string name)
    {
        if (!string.IsNullOrWhiteSpace(ct) && ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    // =========================
    // PREVIEW INLINE (iframe wrapper)
    // =========================
    [HttpGet]
    public async Task<IActionResult> PreviewInline(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (IsImage(v.ContentType, v.FileName))
            {
                var url = Url.Action("PreviewVersion", "Ged", new { versionId })!;
                var html = $@"
<!doctype html>
<html>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<style>
  html,body{{height:100%;margin:0;background:#f5f5f7;}}
  .wrap{{height:100%;display:flex;align-items:center;justify-content:center;overflow:auto;}}
  img{{max-width:100%;max-height:100%;}}
</style>
</head>
<body>
<div class='wrap'><img src='{url}' alt='preview' /></div>
</body>
</html>";
                return Content(html, "text/html", Encoding.UTF8);
            }

            var previewUrl = Url.Action("PreviewVersion", "Ged", new { versionId })!;
            var htmlPdf = $@"
<!doctype html>
<html>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<style>html,body{{height:100%;margin:0;}} iframe{{width:100%;height:100%;border:0;}}</style>
</head>
<body>
<iframe src='{previewUrl}'></iframe>
</body>
</html>";
            return Content(htmlPdf, "text/html", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em PreviewInline. VersionId={VersionId}", versionId);
            return StatusCode(500);
        }
    }

    // =========================
    // PREVIEW VERSION (inline) - usado no iframe
    // =========================
    [HttpGet]
    public async Task<IActionResult> PreviewVersion(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var range = Request.Headers.Range.ToString();
        var sw = Stopwatch.StartNew();

        try
        {
            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            Response.Headers[HeaderNames.CacheControl] = "private, max-age=120";
            Response.Headers[HeaderNames.LastModified] = DateTimeOffset.UtcNow.ToString("R");

            if (IsImage(v.ContentType, v.FileName))
            {
                var img = await _storage.OpenReadAsync(v.StoragePath, ct);
                SetInlineContentDisposition(v.FileName);

                _logger.LogInformation("GED preview streaming. Tenant={TenantId} User={UserId} VersionId={VersionId} FileSize={FileSize} ContentType={ContentType} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, v.SizeBytes, v.ContentType, range, sw.ElapsedMilliseconds, false);
                return File(
                    img,
                    string.IsNullOrWhiteSpace(v.ContentType) ? "image/*" : v.ContentType,
                    enableRangeProcessing: true
                );
            }

            if (IsPdf(v.ContentType, v.FileName))
            {
                var pdf = await _storage.OpenReadAsync(v.StoragePath, ct);
                SetInlineContentDisposition(v.FileName);

                _logger.LogInformation("GED preview streaming. Tenant={TenantId} User={UserId} VersionId={VersionId} FileSize={FileSize} ContentType={ContentType} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, v.SizeBytes, "application/pdf", range, sw.ElapsedMilliseconds, false);
                return File(pdf, "application/pdf", enableRangeProcessing: true);
            }

            var previewPath = BuildPreviewPath(tenantId, v.DocumentId, versionId, v.FileName);
            if (await _storage.ExistsAsync(previewPath, ct))
            {
                var preview = await _storage.OpenReadAsync(previewPath, ct);

                var previewName = $"{Path.GetFileNameWithoutExtension(v.FileName)}.pdf";
                SetInlineContentDisposition(previewName);

                _logger.LogInformation("GED preview PDF streaming. Tenant={TenantId} User={UserId} VersionId={VersionId} FileSize={FileSize} ContentType={ContentType} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, v.SizeBytes, "application/pdf", range, sw.ElapsedMilliseconds, false);
                return File(preview, "application/pdf", enableRangeProcessing: true);
            }

            await _previewStatus.UpsertAsync(tenantId, versionId, PreviewProcessingStatus.Pending, null, null, DateTimeOffset.UtcNow, null, ct);
            await _previewQueue.EnqueueAsync(tenantId, v.DocumentId, versionId, v.StoragePath, v.FileName, ct);
            return PreviewProcessingHtml(versionId);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "GED preview abortado pelo cliente. Tenant={TenantId} User={UserId} VersionId={VersionId} Range={Range} ElapsedMs={ElapsedMs} Aborted={Aborted}", tenantId, userId, versionId, range, sw.ElapsedMilliseconds, true);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em PreviewVersion. Tenant={TenantId} User={UserId} VersionId={VersionId} Range={Range} ElapsedMs={ElapsedMs}", tenantId, userId, versionId, range, sw.ElapsedMilliseconds);
            return PreviewErrorHtml(versionId);
        }
    }

    private ContentResult PreviewProcessingHtml(Guid versionId)
    {
        var retryUrl = Url.Action("PreviewVersion", "Ged", new { versionId }) ?? $"/Ged/PreviewVersion?versionId={versionId}";
        var statusUrl = Url.Action("PreviewStatus", "Ged", new { versionId }) ?? $"/Ged/PreviewStatus?versionId={versionId}";

        var html = $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Gerando visualização…</title>
  <style>
    body{{font-family:system-ui;margin:0;background:#f6f7fb;color:#222}}
    .box{{max-width:720px;margin:10vh auto;padding:24px;background:#fff;border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.08)}}
    .muted{{color:#666}}
    .spinner{{width:24px;height:24px;border:3px solid #ddd;border-top-color:#333;border-radius:50%;animation:spin 1s linear infinite;display:inline-block;vertical-align:middle;margin-right:10px}}
    @keyframes spin{{to{{transform:rotate(360deg)}}}}
    a{{color:#0b5ed7}}
  </style>
</head>
<body>
  <div class=""box"">
    <div><span class=""spinner""></span><strong>Gerando / atualizando visualização…</strong></div>
    <p class=""muted"">Isso pode levar alguns segundos. A página vai tentar novamente automaticamente.</p>
    <p class=""muted"">Se demorar muito, clique em <a href=""{retryUrl}"">tentar novamente</a>.</p>
  </div>
  <script>
    const statusUrl = ""{statusUrl}"";
    let wait = 3000;
    async function tick() {{
      const res = await fetch(statusUrl, {{ headers: {{ Accept: ""application/json"" }} }});
      if (res.ok) {{
        const data = await res.json();
        if (data.status === ""READY"" && data.previewUrl) {{ location.href = data.previewUrl; return; }}
        if (data.status === ""ERROR"") {{ return; }}
      }}
      setTimeout(tick, wait);
      wait = Math.min(wait * 1.7, 10000);
    }}
    setTimeout(tick, wait);
  </script>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }

    private ContentResult PreviewErrorHtml(Guid versionId)
    {
        var retryUrl = Url.Action("PreviewVersion", "Ged", new { versionId }) ?? $"/Ged/PreviewVersion?versionId={versionId}";

        var html = $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Falha na visualização</title>
  <style>
    body{{font-family:system-ui;margin:0;background:#f6f7fb;color:#222}}
    .box{{max-width:720px;margin:10vh auto;padding:24px;background:#fff;border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.08)}}
    .muted{{color:#666}} a{{color:#0b5ed7}}
  </style>
</head>
<body>
  <div class=""box"">
    <h3>Falha ao gerar a visualização</h3>
    <p class=""muted"">O servidor registrou um erro ao converter o arquivo para PDF. Verifique o log para detalhes.</p>
    <p class=""muted"">Tente novamente: <a href=""{retryUrl}"">recarregar</a></p>
  </div>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet]
    public async Task<IActionResult> PreviewStatus(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        if (versionId == Guid.Empty) return BadRequest(new { message = "VersionId inválido." });

        try
        {
            var tenantId = _currentUser.TenantId;
            var status = await _previewStatus.GetAsync(tenantId, versionId, ct);
            if (status is null)
                return Ok(new { success = true, versionId, status = "NOT_READY", previewPath = (string?)null, errorMessage = (string?)null, attempts = 0, lastUpdatedAt = (DateTimeOffset?)null });

            var previewUrl = status.Status == PreviewProcessingStatus.Ready && !string.IsNullOrWhiteSpace(status.PreviewPath)
                ? $"/storage/{status.PreviewPath}"
                : null;
            return Ok(new
            {
                success = true,
                versionId,
                status = status.Status.ToString().ToUpperInvariant(),
                previewPath = status.PreviewPath,
                previewUrl,
                errorMessage = status.ErrorMessage,
                attempts = 0,
                lastUpdatedAt = status.FinishedAt ?? status.RequestedAt,
                requestedAt = status.RequestedAt,
                finishedAt = status.FinishedAt
            });
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Consulta PreviewStatus abortada pelo cliente. Tenant={TenantId} Version={VersionId}", _currentUser.TenantId, versionId);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado em PreviewStatus. Tenant={TenantId} Version={VersionId}", _currentUser.TenantId, versionId);
            return Ok(new { success = false, versionId, status = "ERROR", errorMessage = "Não foi possível consultar o status do OCR no momento." });
        }
    }

    [HttpGet("/Ged/Folders/Search")]
    public async Task<IActionResult> SearchFolders([FromQuery] string? term, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;
            var rows = await _documentMoveService.SearchFoldersAsync(tenantId, userId, term, ct);
            return Ok(new { success = true, items = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Erro ao buscar pastas para movimentação. Tenant={TenantId} User={UserId} Term={Term}",
                _currentUser.TenantId,
                _currentUser.UserId,
                term);

            return Ok(new
            {
                success = false,
                message = "Não foi possível buscar pastas no banco. Usando fallback visual quando disponível.",
                items = Array.Empty<object>()
            });
        }
    }

    [HttpPost("/Ged/Documents/Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move([FromBody] DocumentMoveRequestVM request, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var isAdmin = User.IsInRole(AppRoles.Admin);
            _logger.LogInformation("Upload/drop destino resolvido. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} UploadFolderId={UploadFolderId} Source={Source} DocumentIds={DocumentIds} FileCount={FileCount}", _currentUser.TenantId, _currentUser.UserId, request.RequestedFolderId ?? request.DestinationFolderId, request.DestinationFolderId, request.Source ?? "SINGLE", request.DocumentId, 0);
            var result = await _documentMoveService.MoveAsync(_currentUser.TenantId, _currentUser.UserId, User.Identity?.Name, request.DocumentId, request.DestinationFolderId, request.Reason, request.Source ?? "SINGLE", isAdmin, ct);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível mover o documento." });
            return Ok(new { success = true, message = "Documento movido com sucesso.", data = result.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Move. Tenant={TenantId} User={UserId} Document={DocumentId} Destination={DestinationFolderId}", _currentUser.TenantId, _currentUser.UserId, request.DocumentId, request.DestinationFolderId);
            return StatusCode(500, new { success = false, message = "Erro interno ao mover documento." });
        }
    }

    [HttpPost("/Ged/Documents/MoveBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveBulk([FromBody] DocumentBulkMoveRequestVM request, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var isAdmin = User.IsInRole(AppRoles.Admin);
            _logger.LogInformation("Upload/drop destino resolvido. Tenant={TenantId} User={UserId} RequestedFolderId={RequestedFolderId} UploadFolderId={UploadFolderId} Source={Source} DocumentIds={DocumentIds} FileCount={FileCount}", _currentUser.TenantId, _currentUser.UserId, request.RequestedFolderId ?? request.DestinationFolderId, request.DestinationFolderId, request.Source ?? "BULK", string.Join(",", request.DocumentIds), 0);
            var result = await _documentMoveService.MoveBulkAsync(_currentUser.TenantId, _currentUser.UserId, User.Identity?.Name, request.DocumentIds, request.DestinationFolderId, request.Reason, request.Source ?? "BULK", isAdmin, ct);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível mover o documento." });
            return Ok(new { success = true, message = "Movimentação em lote concluída.", data = result.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em MoveBulk. Tenant={TenantId} User={UserId} Destination={DestinationFolderId}", _currentUser.TenantId, _currentUser.UserId, request.DestinationFolderId);
            return StatusCode(500, new { success = false, message = "Erro interno ao mover documento." });
        }
    }

    [HttpGet("/Ged/Documents/{id:guid}/MoveHistory")]
    public async Task<IActionResult> MoveHistory(Guid id, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var rows = await _documentMoveService.GetMoveHistoryAsync(_currentUser.TenantId, id, ct);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em MoveHistory. Tenant={TenantId} User={UserId} Document={DocumentId}", _currentUser.TenantId, _currentUser.UserId, id);
            return Ok(Array.Empty<DocumentMoveHistoryDto>());
        }
    }

    // =========================
    // RUN OCR (enfileira)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunOcr(Guid versionId, bool force = false, CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return Unauthorized();

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null)
                return NotFound();

            var alreadyCompleted = await _ocrJobs.HasCompletedAsync(tenantId, versionId, ct);
            var latestStatus = await _ocrJobs.GetLatestByVersionIdAsync(tenantId, versionId, ct);

            var alreadyRunning = latestStatus is not null &&
                                 (string.Equals(latestStatus.Status.ToString(), "PENDING", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(latestStatus.Status.ToString(), "PROCESSING", StringComparison.OrdinalIgnoreCase));

            if (alreadyRunning)
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        success = true,
                        alreadyRunning = true,
                        message = "OCR já está em processamento para esta versão.",
                        versionId,
                        jobId = latestStatus!.JobId
                    });
                }

                TempData["Success"] = "OCR já está em processamento para esta versão.";
                return RedirectToAction(nameof(Details), new { id = v.DocumentId, versionId });
            }

            if (alreadyCompleted && !force)
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        success = true,
                        alreadyCompleted = true,
                        message = "OCR já foi executado para esta versão.",
                        versionId
                    });
                }

                TempData["Success"] = "OCR já foi executado para esta versão.";
                return RedirectToAction(nameof(Details), new { id = v.DocumentId, versionId });
            }

            var jobId = await _ocrJobs.EnqueueAsync(
                tenantId,
                versionId,
                userId,
                invalidateDigitalSignatures: force,
                ct);

            await _ocrNotifier.PublishOcrStatusAsync(tenantId, versionId, "PENDING", jobId, "OCR enfileirado", ct);

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = "OCR solicitado. O processamento será executado em segundo plano.",
                    versionId,
                    jobId
                });
            }

            TempData["Success"] = "OCR solicitado. Acompanhe o status na lista de versões.";

            return RedirectToAction(nameof(Details), new
            {
                id = v.DocumentId,
                versionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao solicitar OCR. VersionId={VersionId}", versionId);

            if (IsAjaxRequest())
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Erro ao solicitar OCR."
                });
            }

            TempData["Error"] = "Erro ao solicitar OCR.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprocessOcr(Guid versionId, CancellationToken ct = default)
    {
        return await RunOcr(versionId, force: true, ct);
    }

    [HttpGet]
    public async Task<IActionResult> OcrStatus(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;

            var status = await _ocrJobs.GetLatestByVersionIdAsync(tenantId, versionId, ct);

            if (status is null)
            {
                return Json(new
                {
                    success = true,
                    hasJob = false,
                    status = "NONE",
                    label = "Não executado",
                    completed = false,
                    error = false
                });
            }

            var st = status.Status.ToString().ToUpperInvariant();

            return Json(new
            {
                success = true,
                hasJob = true,
                versionId,
                jobId = status.JobId,
                status = st,
                label = st switch
                {
                    "PENDING" => "Pendente",
                    "PROCESSING" => "Executando",
                    "COMPLETED" => "Concluído",
                    "ERROR" => "Erro",
                    "CANCELLED" => "Cancelado",
                    _ => st
                },
                requestedAt = status.RequestedAt,
                startedAt = status.StartedAt,
                finishedAt = status.FinishedAt,
                errorMessage = status.ErrorMessage,
                completed = st == "COMPLETED",
                error = st == "ERROR"
            });
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Consulta OcrStatus abortada pelo cliente. Tenant={TenantId} VersionId={VersionId}", _currentUser.TenantId, versionId);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar status OCR. VersionId={VersionId}", versionId);

            return StatusCode(500, new
            {
                success = false,
                message = "Erro ao consultar status OCR."
            });
        }
    }

    private async Task InsertOcrRequestAuditAsync(
        Guid tenantId,
        Guid documentId,
        Guid? userId,
        long jobId,
        Guid versionId,
        bool force,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.document_classification_audit
(
  id, tenant_id, document_id, user_id,
  action, method,
  before_json, after_json,
  source, created_at, reg_status
)
VALUES
(
  gen_random_uuid(), @tenantId, @documentId, @userId,
  'OCR_REQUESTED', 'OCR',
  '{}'::jsonb,
  jsonb_build_object(
      'jobId', @jobId,
      'versionId', @versionId,
      'force', @force
  ),
  'WEB', now(), 'A'
);";

        using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    documentId,
                    userId,
                    jobId,
                    versionId,
                    force
                },
                cancellationToken: ct));
    }

    [HttpGet]
    public async Task<IActionResult> OcrText(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            await WriteGedAuditAsync("VIEW", "DOCUMENT_OCR", v.DocumentId, "OCR aberto no GED", new { versionId, v.DocumentId, v.FileName }, ct);

            var ocrRelPath = Path.Combine(
                tenantId.ToString("N"),
                "ocr",
                v.DocumentId.ToString("N"),
                versionId.ToString("N"),
                "ocr.txt"
            ).Replace('\\', '/');

            if (!await _storage.ExistsAsync(ocrRelPath, ct))
                return NotFound("OCR ainda não gerado.");

            var stream = await _storage.OpenReadAsync(ocrRelPath, ct);
            return File(stream, "text/plain; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em OcrText. VersionId={VersionId}", versionId);
            return StatusCode(500);
        }
    }

    // =========================
    // SEARCH (FULL-TEXT)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Search(string? q, Guid? folderId, string? scope = "folder", int limit = 25, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = HttpContext.TraceIdentifier;
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            if (!await _accessPolicy.CanAccessGedAsync(tenantId, userId, User, ct))
                return Forbid();

            if (FolderIdHelper.IsVirtualFolder(folderId))
                folderId = null;

            var isGlobal = string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase);
            var result = await _smartSearch.SearchAsync(new SmartSearchRequest
            {
                TenantId = tenantId,
                UserId = userId,
                Query = q ?? string.Empty,
                FolderId = folderId,
                Scope = isGlobal ? "global" : "folder",
                Module = "GED",
                Limit = limit,
                IsAdmin = User.IsInRole("ADMIN") || _currentUser.Roles.Any(r => string.Equals(r, "ADMIN", StringComparison.OrdinalIgnoreCase))
            }, ct);

            var rows = result.Items.Select(x => new DocumentSearchRowDto(
                x.DocumentId,
                x.VersionId ?? Guid.Empty,
                x.DocumentId.ToString(),
                x.Title,
                x.OriginalFileName ?? string.Empty,
                x.OcrSnippet ?? "Documento encontrado pelos filtros informados.",
                (float)x.Score)).ToList();

            sw.Stop();
            _logger.LogInformation("GED Search executado. Tenant={TenantId} User={UserId} FolderId={FolderId} Scope={Scope} Query={Query} ResultCount={ResultCount} ElapsedMs={ElapsedMs} Module={Module} CorrelationId={CorrelationId}",
                tenantId, userId, folderId, isGlobal ? "global" : "folder", q, rows.Count, sw.ElapsedMilliseconds, "GED", correlationId);
            await _auditWriter.WriteAsync(tenantId, userId, "VIEW", "GED_SEARCH", null, "Busca GED executada", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { query = q, folderId, scope = isGlobal ? "global" : "folder", resultCount = rows.Count, elapsedMs = sw.ElapsedMilliseconds, module = "GED", correlationId }, ct);

            ViewBag.Query = q ?? "";
            ViewBag.FolderId = folderId;
            ViewBag.Scope = isGlobal ? "global" : "folder";

            return View("Search", rows);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation(ex, "GED Search cancelado pelo cliente. Tenant={TenantId} User={UserId} FolderId={FolderId} Query={Query} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                tenantId, userId, folderId, q, sw.ElapsedMilliseconds, correlationId);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Erro em GED Search. Tenant={TenantId} User={UserId} FolderId={FolderId} Query={Query} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                tenantId, userId, folderId, q, sw.ElapsedMilliseconds, correlationId);
            ViewBag.Query = q ?? "";
            ViewBag.FolderId = folderId;
            ViewBag.SearchError = "Não foi possível executar a busca. Tente novamente ou refine os filtros.";
            return View("Search", Array.Empty<DocumentSearchRowDto>());
        }
    }

    

    // =========================
    // REGENERATE PREVIEW
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegeneratePreview(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (IsImage(v.ContentType, v.FileName) || IsPdf(v.ContentType, v.FileName))
                return RedirectToAction(nameof(Details), new { id = v.DocumentId });

            var previewRelPath = BuildPreviewPath(tenantId, v.DocumentId, versionId, v.FileName);

            await _storage.DeleteIfExistsAsync(previewRelPath, ct);
            await _previewStatus.UpsertAsync(tenantId, versionId, PreviewProcessingStatus.Pending, null, null, DateTimeOffset.UtcNow, null, ct);
            await _previewQueue.EnqueueAsync(tenantId, v.DocumentId, versionId, v.StoragePath, v.FileName, ct);

            TempData["ok"] = "Preview enfileirado para regeração.";
            return RedirectToAction(nameof(Details), new { id = v.DocumentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em RegeneratePreview. VersionId={VersionId}", versionId);
            TempData["erro"] = "Erro ao regerar preview.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenomearPastaExplorer(Guid id, string nome, Guid? folderId, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            var result = await _folderCommands.RenameAsync(
                tenantId: tenantId,
                folderId: id,
                newName: nome,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta renomeada com sucesso." : (result.Error?.Message ?? "Falha ao renomear pasta.");

            return RedirectToAction(nameof(Index), new { folderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao renomear pasta (Explorer). FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao renomear pasta.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid? folderId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                return Unauthorized(new { success = false, message = "Usuário não autenticado." });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;
            var isAdmin = await _accessPolicy.IsAdminAsync(tenantId, userId, User, ct);

            var result = await _documentCommands.DeleteAsync(tenantId, id, userId, isAdmin, ct);
            if (!result.Success)
            {
                return BadRequest(new { success = false, message = result.Error?.Message ?? "Não foi possível excluir o documento." });
            }

            var message = isAdmin
                ? "Documento excluído com sucesso. Processamentos de OCR relacionados foram cancelados."
                : "Documento excluído com sucesso.";

            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em DeleteDocument. Tenant={TenantId} User={UserId} DocId={DocId} FolderId={FolderId}", _currentUser.TenantId, _currentUser.UserId, id, folderId);
            return StatusCode(500, new { success = false, message = "Não foi possível excluir o documento." });
        }
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StopOcrQueue(Guid? documentId, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;
            var affected = await _ocrJobs.CancelQueueAsync(tenantId, documentId, _currentUser.UserId, "Fila interrompida por ADMIN", ct);
            TempData["ok"] = affected > 0 ? $"OCR interrompido para {affected} job(s)." : "Nenhum job OCR ativo encontrado.";
            return RedirectToAction(nameof(Processing));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao parar fila OCR. Tenant={TenantId} DocumentId={DocumentId}", _currentUser.TenantId, documentId);
            TempData["erro"] = "Erro ao parar fila OCR.";
            return RedirectToAction(nameof(Processing));
        }
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOcrJob(long jobId, Guid versionId, CancellationToken ct)
    {
        try
        {
            if (jobId <= 0 || versionId == Guid.Empty) return BadRequest();
            var affected = await _ocrJobs.CancelByVersionAsync(_currentUser.TenantId, versionId, _currentUser.UserId, $"Cancelado por ADMIN (job {jobId})", ct);
            TempData[affected > 0 ? "ok" : "erro"] = affected > 0 ? "Job OCR cancelado." : "Nenhum job elegível para cancelamento.";
            return RedirectToAction(nameof(Processing));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao cancelar job OCR. Tenant={TenantId} JobId={JobId} VersionId={VersionId}", _currentUser.TenantId, jobId, versionId);
            TempData["erro"] = "Erro ao cancelar job OCR.";
            return RedirectToAction(nameof(Processing));
        }
    }

  
    // =========================
    // Content-Disposition seguro
    // =========================
    private void SetInlineContentDisposition(string fileName)
    {
        fileName = SanitizeFileName(fileName);

        var asciiFallback = ToAsciiFileName(fileName);
        if (string.IsNullOrWhiteSpace(asciiFallback))
            asciiFallback = "preview";

        var cd = new ContentDispositionHeaderValue("inline")
        {
            FileName = QuoteIfNeeded(asciiFallback),
            FileNameStar = fileName
        };

        Response.Headers[HeaderNames.ContentDisposition] = cd.ToString();
    }

    private static string BuildPreviewPath(Guid tenantId, Guid documentId, Guid versionId, string originalFileName)
    {
        var previewName = $"{Path.GetFileNameWithoutExtension(SanitizeFileName(originalFileName))}.pdf";
        return Path.Combine(tenantId.ToString("N"), "previews", documentId.ToString("N"), versionId.ToString("N"), previewName).Replace('\\', '/');
    }

    private static string SanitizeFileName(string fileName)
    {
        fileName = (fileName ?? "").Trim()
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\"", "'");

        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        if (fileName.Length > 180)
            fileName = fileName[..180];

        return string.IsNullOrWhiteSpace(fileName) ? "documento" : fileName;
    }

    private static string ToAsciiFileName(string fileName)
    {
        var normalized = fileName.Normalize(NormalizationForm.FormD);

        Span<char> buffer = stackalloc char[normalized.Length];
        var idx = 0;

        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc == UnicodeCategory.NonSpacingMark) continue;

            if (ch >= 32 && ch <= 126)
                buffer[idx++] = ch;
        }

        var ascii = new string(buffer[..idx]);

        foreach (var bad in Path.GetInvalidFileNameChars())
            ascii = ascii.Replace(bad.ToString(), "");

        return ascii.Replace(";", "_").Replace(",", "_").Trim();
    }

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ') ? $"\"{value}\"" : value;
}
