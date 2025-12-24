using System.Security.Claims;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Identity;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using InovaGed.Web.Models.Ged;
using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;
using InovaGed.Infrastructure.Documents;
using InovaGed.Application;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class GedController : Controller
{
    private readonly ILogger<GedController> _logger;
    private readonly ICurrentUser _currentUser;

    private readonly IFolderQueries _folders;
    private readonly IFolderCommands _folderCommands;

    private readonly IDocumentQueries _docs;
    private readonly DocumentAppService _documentApp;
    private readonly IFileStorage _storage;

    // Workflow
    private readonly IWorkflowQueries _workflowQ;
    private readonly IWorkflowCommands _workflowC;
    private readonly IDocumentWorkflowQueries _docWfQ;
    private readonly IDocumentWorkflowCommands _docWfC;
    private readonly IPreviewGenerator _preview;
    public GedController(
        ILogger<GedController> logger,
        ICurrentUser currentUser,
        IFolderQueries folders,
        IFolderCommands folderCommands,
        IDocumentQueries docs,
        DocumentAppService documentApp,
        IFileStorage storage,
        IPreviewGenerator preview, 
        IWorkflowQueries workflowQ,
        IWorkflowCommands workflowC,
        IDocumentWorkflowQueries docWfQ,
        IDocumentWorkflowCommands docWfC)
    {
        _logger = logger;
        _currentUser = currentUser;

        _folders = folders;
        _folderCommands = folderCommands;

        _docs = docs;
        _documentApp = documentApp;
        _storage = storage;
        _preview = preview;

        _workflowQ = workflowQ;
        _workflowC = workflowC;
        _docWfQ = docWfQ;
        _docWfC = docWfC;
    }

    // =========================
    // EXPLORER
    // =========================
    [HttpGet]
    public async Task<IActionResult> Index(Guid? folderId, string? q, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return RedirectToAction("Login", "Account");

        var tenantId = _currentUser.TenantId;

        var tree = await _folders.TreeAsync(tenantId, ct);

        // se não veio folderId, tenta apontar para a "primeira" pasta da árvore
        var effectiveFolderId = folderId ?? tree.FirstOrDefault()?.Id;

        var docs = effectiveFolderId.HasValue
            ? await _docs.ListAsync(tenantId, effectiveFolderId.Value, q, ct)
            : Array.Empty<DocumentRowDto>();

        var vm = new GedExplorerVM
        {
            CurrentFolderId = effectiveFolderId, // <<< ESSENCIAL
            FolderId = effectiveFolderId,         // (se você tiver essa prop também, mantém as 2)
            Query = q,
            Folders = tree.Select(x => new GedExplorerVM.FolderNodeVM
            {
                Id = x.Id,
                ParentId = x.ParentId,
                Name = x.Name,
                Level = x.Level
            }).ToList(),
            Documents = docs.Select(d => new GedExplorerVM.DocumentRowVM
            {
                Id = d.Id,
                Title = d.Title,
                TypeName = d.TypeName,
                FileName = d.FileName,
                SizeBytes = d.SizeBytes,
                CreatedAt = d.CreatedAt,
                CreatedBy = d.CreatedBy,
                IsConfidential = d.IsConfidential
            }).ToList()
        };

        return View(vm);

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
            _logger.LogError(ex, "Erro ao criar pasta.");
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
        CancellationToken ct)
    {
        try
        {
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

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();

            await using var stream = file.OpenReadStream();

            var cmd = new UploadDocumentCommand
            {
                FolderId = folderId,
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title.Trim(),
                Description = null,
                IsConfidential = isConfidential ?? false,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = stream
            };

            var result = await _documentApp.UploadAsync(cmd, ip, ua, ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha no upload.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            TempData["Success"] = "Upload realizado com sucesso.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no upload do GED. FolderId={FolderId}", folderId);
            TempData["Error"] = "Erro ao enviar arquivo.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
    }

    // =========================
    // DETAILS
    // =========================
    [HttpGet]
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            var doc = await _docs.GetAsync(tenantId, id, ct);
            if (doc is null) return NotFound();

            var versions = await _docs.ListVersionsAsync(tenantId, id, ct);

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
                    CreatedBy = v.CreatedBy,
                    IsCurrent = doc.CurrentVersionId.HasValue && v.Id == doc.CurrentVersionId.Value
                }).ToList()
            };

            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar detalhes do documento. DocId={DocId}", id);
            TempData["Error"] = "Erro ao carregar detalhes do documento.";
            return RedirectToAction(nameof(Index));
        }
    }


    // =========================
    // DOWNLOAD (por versionId)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            TempData["Error"] = "Usuário não autenticado.";
            return RedirectToAction(nameof(Index));
        }

        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, id, ct);
        if (v == null) return NotFound();

        if (!await _storage.ExistsAsync(v.StoragePath, ct))
            return NotFound("Arquivo não encontrado no storage.");

        var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
        var contentType = string.IsNullOrWhiteSpace(v.ContentType) ? "application/octet-stream" : v.ContentType;

        // download forçado
        return File(stream, contentType, v.FileName);
    }

    // =========================
    // PREVIEW (inline) - usado no iframe
    // =========================
    [HttpGet]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, id, ct);
        if (v == null) return NotFound();

        if (!await _storage.ExistsAsync(v.StoragePath, ct))
            return NotFound("Arquivo não encontrado no storage.");

        var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
        var contentType = string.IsNullOrWhiteSpace(v.ContentType) ? "application/octet-stream" : v.ContentType;

        // inline (para visualização)
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{v.FileName}\"";
        return File(stream, contentType);
    }

    // =========================
    // WORKFLOW - INICIAR
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
            _logger.LogError(ex, "Erro ao iniciar workflow. Doc={DocId}, Workflow={WorkflowId}", documentId, workflowId);
            TempData["Error"] = "Erro ao iniciar workflow.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
    }

    // =========================
    // WORKFLOW - APLICAR TRANSIÇÃO
    // =========================
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

            var result = await _docWfC.ApplyTransitionAsync(
                tenantId,
                documentId,      // documentWorkflowId (no seu domínio é o doc atual)
                transitionId,
                reason,          // ✅ reason
                comments,        // ✅ comments
                userId,          // ✅ userId na posição correta
                ct               // ✅ CancellationToken
            );

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
            _logger.LogError(ex,
                "Erro ao aplicar transição. Doc={DocId}, Transition={TransitionId}",
                documentId, transitionId);

            TempData["Error"] = "Erro ao aplicar transição.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadVersion(Guid versionId, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
        return File(stream, v.ContentType, v.FileName);
    }

    //[HttpGet]
    //public async Task<IActionResult> PreviewVersion(Guid versionId, CancellationToken ct)
    //{
    //    var tenantId = _currentUser.TenantId;

    //    var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
    //    if (v is null) return NotFound();

    //    var stream = await _storage.OpenReadAsync(v.StoragePath, ct);

    //    Response.Headers["Content-Disposition"] = $"inline; filename=\"{v.FileName}\"";
    //    return File(stream, v.ContentType);
    //}

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
                    Name = x.Name,
                    Level = x.Level
                }).ToList()
            };

            return View(vm); // 👈 AGORA BATE COM A VIEW
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
                departmentId: null, // 👈 não quebra
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

            var result = await _folderCommands.DeactivateAsync(
                tenantId: tenantId,
                folderId: id,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta desativada com sucesso." : (result.Error?.Message ?? "Falha ao desativar pasta.");

            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desativar pasta. FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao desativar pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpGet]
    public async Task<IActionResult> PreviewVersion(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        if (!await _storage.ExistsAsync(v.StoragePath, ct))
            return NotFound("Arquivo não encontrado no storage.");

        // ✅ imagem = imagem
        if (IsImage(v.ContentType, v.FileName))
        {
            var img = await _storage.OpenReadAsync(v.StoragePath, ct);
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{v.FileName}\"";
            return File(img, string.IsNullOrWhiteSpace(v.ContentType) ? "image/*" : v.ContentType);
        }

        // ✅ pdf = pdf
        if (IsPdf(v.ContentType, v.FileName))
        {
            var pdf = await _storage.OpenReadAsync(v.StoragePath, ct);
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{v.FileName}\"";
            return File(pdf, "application/pdf");
        }

        // ✅ tudo que não for imagem: converte -> pdf
        var previewPath = await _preview.GetOrCreatePreviewPdfAsync(
            tenantId,
            v.DocumentId,
            versionId,
            v.StoragePath,
            v.FileName,
            ct);

        var preview = await _storage.OpenReadAsync(previewPath, ct);
        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{Path.GetFileNameWithoutExtension(v.FileName)}.pdf\"";

        return File(preview, "application/pdf");
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


    //private static bool IsPdf(string? ct, string name)
    //    => (!string.IsNullOrWhiteSpace(ct) && ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
    //       || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcel(string? ct, string name)
        => name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
           || (!string.IsNullOrWhiteSpace(ct) && (ct.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) || ct.Contains("excel", StringComparison.OrdinalIgnoreCase)));

    //private static bool IsImage(string? ct, string name)
    //{
    //    if (!string.IsNullOrWhiteSpace(ct) && ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    //        return true;

    //    var ext = Path.GetExtension(name).ToLowerInvariant();
    //    return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    //}


}
