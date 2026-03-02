using System.Security.Claims;
using InovaGed.Application.Common.Context; // se existir; senão remova
using InovaGed.Application.Instruments;
using InovaGed.Infrastructure.Instruments;
using InovaGed.Web.Models.Instruments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
// [Authorize] // habilite se quiser. Na PoC você pode deixar sem permissão por enquanto.
public sealed class InstrumentsController : Controller
{
    private readonly IInstrumentRepository _repo;
    private readonly ILogger<InstrumentsController> _logger;

    // Se você tiver ICurrentContext (multi-tenant) no seu projeto, injete aqui.
    private readonly ICurrentContext? _ctx;

    public InstrumentsController(
        IInstrumentRepository repo,
        ILogger<InstrumentsController> logger,
        IServiceProvider sp)
    {
        _repo = repo;
        _logger = logger;

        // tenta resolver ICurrentContext se existir no container
        _ctx = sp.GetService(typeof(ICurrentContext)) as ICurrentContext;
    }

    private Guid TenantIdOrThrow()
    {
        var tid = _ctx?.TenantId ?? Guid.Empty;
        if (tid == Guid.Empty)
            throw new InvalidOperationException("TenantId não encontrado no contexto (_ctx.TenantId).");
        return tid;
    }

    private Guid? UserId()
    {
        // preferencial
        var uid = _ctx?.UserId;
        if (uid.HasValue && uid.Value != Guid.Empty) return uid;

        // fallback claim
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(s, out var g)) return g;
        return null;
    }

    private string? UserDisplay()
    {
        var d = _ctx?.UserDisplay;
        if (!string.IsNullOrWhiteSpace(d)) return d;

        return User.Identity?.Name;
    }

    // GET /Instruments?type=PCD
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? type, CancellationToken ct)
    {
        type = NormalizeType(type);

        var tenantId = TenantIdOrThrow();
        var versions = await _repo.ListVersionsAsync(tenantId, type, ct);

        var vm = new InstrumentsIndexVM
        {
            Type = type,
            Versions = versions
        };

        return View(vm); // Views/Instruments/Index.cshtml
    }

    // POST /Instruments/Publish
    [HttpPost("Publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish([FromForm] string type, [FromForm] string? notes, CancellationToken ct)
    {
        type = NormalizeType(type);

        try
        {
            var tenantId = TenantIdOrThrow();

            var verId = await _repo.PublishNewVersionAsync(
                tenantId,
                UserId(),
                UserDisplay(),
                type,
                notes,
                ct);

            TempData["Ok"] = $"{type} publicado com sucesso. VersãoId={verId}";
            return RedirectToAction(nameof(Index), new { type });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish failed Type={Type}", type);
            TempData["Err"] = "Falha ao publicar versão. Veja logs.";
            return RedirectToAction(nameof(Index), new { type });
        }
    }

    // GET /Instruments/Nodes?type=PCD&versionId=...
    [HttpGet("Nodes")]
    public async Task<IActionResult> Nodes([FromQuery] string type, [FromQuery] Guid versionId, [FromQuery] Guid? editId, CancellationToken ct)
    {
        type = NormalizeType(type);

        var tenantId = TenantIdOrThrow();
        var versions = await _repo.ListVersionsAsync(tenantId, type, ct);
        var nodes = await _repo.ListNodesAsync(tenantId, type, versionId, ct);

        InstrumentNodeRow? edit = null;
        if (editId.HasValue)
            edit = nodes.FirstOrDefault(x => x.Id == editId.Value);

        var vm = new InstrumentsNodesVM
        {
            Type = type,
            VersionId = versionId,
            Versions = versions,
            Nodes = nodes,
            Edit = edit
        };

        return View(vm); // Views/Instruments/Nodes.cshtml
    }

    // POST /Instruments/NodeUpsert
    [HttpPost("NodeUpsert")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NodeUpsert([FromForm] InstrumentsNodeUpsertRequest req, CancellationToken ct)
    {
        req.Type = NormalizeType(req.Type);

        try
        {
            var tenantId = TenantIdOrThrow();

            // validações mínimas PoC
            if (req.VersionId == Guid.Empty) throw new ArgumentException("VersionId obrigatório.");
            if (string.IsNullOrWhiteSpace(req.Code)) throw new ArgumentException("Código obrigatório.");
            if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Título obrigatório.");
            if (string.IsNullOrWhiteSpace(req.SecurityLevel)) req.SecurityLevel = "PUBLIC";

            await _repo.UpsertNodeAsync(
                tenantId,
                UserId(),
                UserDisplay(),
                req.Type,
                req.VersionId,
                req.Id,
                req.ParentId,
                req.Code.Trim(),
                req.Title.Trim(),
                string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                req.SortOrder,
                NormalizeSecurity(req.SecurityLevel),
                ct);

            TempData["Ok"] = "Classe/Item salvo com sucesso.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NodeUpsert failed {@Req}", req);
            TempData["Err"] = ex.Message;
        }

        return RedirectToAction(nameof(Nodes), new { type = req.Type, versionId = req.VersionId });
    }

    // POST /Instruments/Move
    [HttpPost("Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move([FromForm] InstrumentsMoveRequest req, CancellationToken ct)
    {
        req.Type = NormalizeType(req.Type);

        try
        {
            var tenantId = TenantIdOrThrow();
            if (req.VersionId == Guid.Empty) throw new ArgumentException("VersionId obrigatório.");
            if (req.NodeId == Guid.Empty) throw new ArgumentException("NodeId obrigatório.");

            await _repo.MoveNodeAsync(
                tenantId,
                UserId(),
                UserDisplay(),
                req.Type,
                req.VersionId,
                req.NodeId,
                req.NewParentId,
                req.NewSortOrder,
                ct);

            TempData["Ok"] = "Movimentação realizada com sucesso.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Move failed {@Req}", req);
            TempData["Err"] = ex.Message;
        }

        return RedirectToAction(nameof(Nodes), new { type = req.Type, versionId = req.VersionId });
    }

    // GET /Instruments/Print?type=PCD&versionId=...
    [HttpGet("Print")]
    public async Task<IActionResult> Print([FromQuery] string type, [FromQuery] Guid versionId, CancellationToken ct)
    {
        type = NormalizeType(type);

        var tenantId = TenantIdOrThrow();
        var html = await _repo.RenderPrintHtmlAsync(tenantId, type, versionId, ct);

        var vm = new InstrumentsPrintVM
        {
            Type = type,
            VersionId = versionId,
            Html = html
        };

        return View(vm); // Views/Instruments/Print.cshtml
    }

    private static string NormalizeType(string? type)
    {
        var t = (type ?? "PCD").Trim().ToUpperInvariant();
        return t switch
        {
            "PCD" => "PCD",
            "TTD" => "TTD",
            "POP" => "POP",
            _ => "PCD"
        };
    }

    private static string NormalizeSecurity(string? s)
    {
        var v = (s ?? "PUBLIC").Trim().ToUpperInvariant();
        return v switch
        {
            "PUBLIC" => "PUBLIC",
            "RESTRICTED" => "RESTRICTED",
            "CONFIDENTIAL" => "CONFIDENTIAL",
            _ => "PUBLIC"
        };
    }
}