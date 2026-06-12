using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ProtocolRequest)]
[Route("ProtocolRequests")]
public sealed class ProtocolRequestsController : Controller
{
    private readonly ICurrentUser _user;
    private readonly IProtocolRequestService _service;
    private readonly IProtocolAccessService _access;
    private readonly IFileStorage _storage;
    private readonly ILogger<ProtocolRequestsController> _logger;

    public ProtocolRequestsController(ICurrentUser user, IProtocolRequestService service, IProtocolAccessService access, IFileStorage storage, ILogger<ProtocolRequestsController> logger)
    { _user = user; _service = service; _access = access; _storage = storage; _logger = logger; }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(My));

    [HttpGet("My")]
    public async Task<IActionResult> My(
        string? q,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page = 1,
        int pageSize = 20,
        bool showAll = false,
        CancellationToken ct = default)
    {
        var filter = new ProtocolWorkQueueFilter
        {
            Search = q,
            Status = status,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
            ShowAll = showAll
        };

        try
        {
            var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
            var rows = await _service.ListMyAsync(_user.TenantId, _user.UserId, scope, filter, ct);
            SetMyViewBag(filter);
            return View(rows);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SyntaxError)
        {
            _logger.LogError(ex, "Erro SQL na tela Minhas Solicitações.");
            SetMyViewBag(filter, "Não foi possível carregar suas solicitações.");
            return View(Array.Empty<ProtocolRequestRowVm>());
        }
    }

    private void SetMyViewBag(ProtocolWorkQueueFilter filter, string? error = null)
    {
        ViewBag.Q = filter.Search;
        ViewBag.Status = filter.Status;
        ViewBag.From = filter.From?.ToString("yyyy-MM-dd");
        ViewBag.To = filter.To?.ToString("yyyy-MM-dd");
        ViewBag.Page = filter.Page;
        ViewBag.PageSize = filter.PageSize;
        ViewBag.ShowAll = filter.ShowAll;
        ViewBag.Error = error;
    }

    [HttpGet("New")]
    public IActionResult New(Guid? documentId) => View(new ProtocolRequestCreateVm { DueAt = DateTimeOffset.Now.AddDays(7), PreselectedDocumentId = documentId, DocumentIds = documentId.HasValue ? new List<Guid> { documentId.Value } : new List<Guid>() });

    [HttpPost("New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(ProtocolRequestCreateVm vm, List<IFormFile>? attachments, CancellationToken ct)
    {
        try
        {
            vm.PendingAttachmentsCount = attachments?.Count(a => a.Length > 0) ?? 0;
            var res = await _service.CreateAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View(vm);
            }

            if (attachments is not null)
            {
                foreach (var file in attachments.Where(f => f.Length > 0))
                {
                    var safe = Path.GetFileName(file.FileName);
                    var path = $"protocols/{_user.TenantId:N}/{res.Value:N}/{Guid.NewGuid():N}_{safe}";
                    await using var stream = file.OpenReadStream();
                    await _storage.SaveDerivedAsync(path, stream, file.ContentType ?? "application/octet-stream", ct);
                    await _service.AddAttachmentAsync(_user.TenantId, res.Value, _user.UserId, safe, file.ContentType, file.Length, path, ct);
                }
            }

            TempData["Ok"] = "Protocolo aberto com sucesso.";
            return RedirectToAction("Details", "Protocols", new { id = res.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao abrir protocolo");
            TempData["Err"] = "Erro ao abrir protocolo.";
            return View(vm);
        }
    }

    [HttpGet("DocSearch")]
    public async Task<IActionResult> DocSearch(string? q, CancellationToken ct)
    {
        var rows = await _service.SearchDocumentsAsync(_user.TenantId, q ?? string.Empty, ct);
        return Json(new { success = true, rows });
    }
}
