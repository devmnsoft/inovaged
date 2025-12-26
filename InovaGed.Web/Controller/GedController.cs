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
using System.Text;

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
    private readonly IOcrService _ocr;
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
        IOcrService ocr,
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
        _ocr = ocr;

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

        try
        {
            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            // ✅ Imagem
            if (IsImage(v.ContentType, v.FileName))
            {
                var img = await _storage.OpenReadAsync(v.StoragePath, ct);
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{v.FileName}\"";
                return File(img,
                    string.IsNullOrWhiteSpace(v.ContentType) ? "image/*" : v.ContentType,
                    enableRangeProcessing: true);
            }

            // ✅ PDF original
            if (IsPdf(v.ContentType, v.FileName))
            {
                var pdf = await _storage.OpenReadAsync(v.StoragePath, ct);
                Response.Headers["Content-Disposition"] = $"inline; filename=\"{v.FileName}\"";
                return File(pdf, "application/pdf", enableRangeProcessing: true);
            }

            // ✅ Outros formatos: tentar preview convertido, mas sem travar a request para sempre
            // Estratégia: gera com timeout; se estourar, devolve HTML "gerando..." que recarrega sozinho.
            var timeoutSeconds = 25;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var previewPath = await _preview.GetOrCreatePreviewPdfAsync(
                    tenantId,
                    v.DocumentId,
                    versionId,
                    v.StoragePath,
                    v.FileName,
                    cts.Token);

                if (!await _storage.ExistsAsync(previewPath, ct))
                    return StatusCode(500, "Falha ao gerar preview (arquivo não encontrado após geração).");

                var preview = await _storage.OpenReadAsync(previewPath, ct);
                Response.Headers["Content-Disposition"] =
                    $"inline; filename=\"{Path.GetFileNameWithoutExtension(v.FileName)}.pdf\"";

                return File(preview, "application/pdf", enableRangeProcessing: true);
            }
            catch (OperationCanceledException)
            {
                // Timeout (ou cancelamento): não deixa o usuário preso.
                var retryUrl = Url.Action("PreviewVersion", "Ged", new { versionId })!;
                var html = $@"
                <!doctype html>
                <html>
                <head>
                  <meta charset='utf-8' />
                  <meta name='viewport' content='width=device-width, initial-scale=1' />
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
                  <div class='box'>
                    <div><span class='spinner'></span><strong>Gerando / atualizando visualização…</strong></div>
                    <p class='muted'>Isso pode levar alguns segundos dependendo do tipo do arquivo. A página vai tentar novamente automaticamente.</p>
                    <p class='muted'>Se demorar muito, clique em <a href='{retryUrl}'>tentar novamente</a> ou use o botão <strong>Regenerar preview</strong>.</p>
                  </div>

                  <script>
                    setTimeout(() => location.href = '{retryUrl}', 2500);
                  </script>
                </body>
                </html>";
                return Content(html, "text/html; charset=utf-8");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em PreviewVersion. Tenant={TenantId}, VersionId={VersionId}", tenantId, versionId);
            return StatusCode(500, "Erro ao gerar/abrir visualização.");
        }
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


    [HttpGet]
    public async Task<IActionResult> PreviewInline(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        // Se for imagem -> mostra <img> com zoom
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
  img{{max-width:100%;max-height:100%;transform:scale(1);transform-origin:center center;transition:transform .08s linear;}}
  .hint{{position:fixed;bottom:10px;left:10px;color:#666;font:12px/1.2 system-ui;}}
</style>
</head>
<body>
<div class='wrap'><img id='img' src='{url}' alt='preview' /></div>
<div class='hint'>Dica: use os botões de zoom na tela principal.</div>
<script>
  // Permite zoom via postMessage vindo do parent
  let scale=1;
  const img=document.getElementById('img');
  window.addEventListener('message',(e)=>{{
    const m=e.data||{{}};
    if(m.type==='zoom'){{ scale=Math.max(0.2,Math.min(5,scale+m.delta)); img.style.transform='scale('+scale+')'; }}
    if(m.type==='zoomReset'){{ scale=1; img.style.transform='scale(1)'; }}
  }});
</script>
</body>
</html>";
            return Content(html, "text/html", Encoding.UTF8);
        }

        // PDF (ou qualquer outro): retorna iframe com PDF (convertido se precisar)
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegeneratePreview(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        // Só faz sentido regerar preview se NÃO for imagem e NÃO for PDF
        if (IsImage(v.ContentType, v.FileName) || IsPdf(v.ContentType, v.FileName))
            return RedirectToAction("Details", new { id = v.DocumentId });

        // Calcula o caminho do preview cacheado (tem que bater com o PreviewGenerator)
        var baseName = Path.GetFileNameWithoutExtension(v.FileName);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "preview";

        var previewRelPath = Path.Combine(
            tenantId.ToString("N"),
            "previews",
            v.DocumentId.ToString("N"),
            versionId.ToString("N"),
            baseName + ".pdf"
        ).Replace('\\', '/');

        // Remove o PDF cacheado (se existir)
        await _storage.DeleteIfExistsAsync(previewRelPath, ct);

        // Força gerar de novo já agora (opcional)
        _ = await _preview.GetOrCreatePreviewPdfAsync(tenantId, v.DocumentId, versionId, v.StoragePath, v.FileName, ct);

        TempData["ok"] = "Preview regerado com sucesso.";
        return RedirectToAction("Details", new { id = v.DocumentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunOcr(Guid versionId, bool force = false, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

        // 1) Determina qual PDF será OCRizado (PDF original ou preview convertido)
        string pdfPath;

        if (IsPdf(v.ContentType, v.FileName))
        {
            pdfPath = v.StoragePath;
        }
        else
        {
            // Word/Excel/PPT -> gera preview PDF e OCRiza o preview
            pdfPath = await _preview.GetOrCreatePreviewPdfAsync(
                tenantId,
                v.DocumentId,
                versionId,
                v.StoragePath,
                v.FileName,
                ct);
        }

        try
        {
            // 2) roda OCR (sem invalidar assinatura se force=false)
            var ocr = await _ocr.OcrizePdfAsync(
                pdfStoragePath: pdfPath,
                invalidateDigitalSignatures: force,
                ct: ct);

            // 3) cria NOVA VERSÃO (PDF pesquisável)
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();

            var newFileName = $"{Path.GetFileNameWithoutExtension(v.FileName)}_OCR.pdf";

            await using var ms = new MemoryStream(ocr.OcrPdfBytes);

            var add = await _documentApp.AddVersionAsync(
                documentId: v.DocumentId,
                content: ms,
                fileName: newFileName,
                contentType: "application/pdf",
                ip: ip,
                userAgent: ua,
                ct: ct);

            if (!add.Success)
            {
                TempData["erro"] = add.Error?.Message ?? "Falha ao salvar nova versão OCR.";
                return RedirectToAction("Details", new { id = v.DocumentId });
            }

            // (Opcional) se você quiser salvar o texto extraído em storage (derivado)
            // Ajuste conforme seu storage (se tiver SaveDerivedAsync):
            // var derivedPath = $"{tenantId:N}/ocr/{v.DocumentId:N}/{add.Value:N}/ocr.txt";
            // await using var txt = new MemoryStream(Encoding.UTF8.GetBytes(ocr.ExtractedText ?? ""));
            // await _storage.SaveDerivedAsync(derivedPath, txt, "text/plain; charset=utf-8", ct);

            TempData["ok"] = force
                ? "OCR concluído (assinatura digital foi invalidada na versão OCR). Nova versão criada!"
                : "OCR concluído. Nova versão criada com sucesso!";

            return RedirectToAction("Details", new { id = v.DocumentId });
        }
        catch (PdfHasDigitalSignatureException)
        {
            // ✅ pede confirmação (sem travar / sem 500)
            TempData["warning"] =
                "Este documento possui assinatura digital. Para criar a versão OCR pesquisável, " +
                "a assinatura da versão OCR será invalidada. Clique em “Forçar OCR” para continuar.";

            // manda também qual versionId disparou o alerta (pra view montar o botão certo)
            TempData["ocr_version_id"] = versionId.ToString();
            return RedirectToAction("Details", new { id = v.DocumentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no OCR. VersionId={VersionId}", versionId);
            TempData["erro"] = "Falha ao executar OCR.";
            return RedirectToAction("Details", new { id = v.DocumentId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> OcrText(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
        if (v is null) return NotFound();

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

   

}
