using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class SolicitacoesController : Controller
{
    private readonly ISolicitacaoService _service;
    private readonly ICurrentUser _user;

    public SolicitacoesController(ISolicitacaoService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var isAdmin = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.AdministradorOphir);
        var setorId = GetSetorId();
        var rows = await _service.ListarParaUsuarioAsync(_user.TenantId, _user.UserId ?? Guid.Empty, setorId, isAdmin, ct);
        ViewBag.PendentesCount = isAdmin ? await _service.PendentesCountAsync(_user.TenantId, ct) : 0;
        return View(rows);
    }

    [HttpGet("Historico")]
    public async Task<IActionResult> Historico(CancellationToken ct)
    {
        var isAdmin = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.AdministradorOphir);
        var rows = await _service.HistoricoAsync(_user.TenantId, _user.UserId, GetSetorId(), isAdmin, ct);
        return View("HistoricoPedidos", rows);
    }

    [HttpPost("Criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(SolicitacaoCreateVM vm, CancellationToken ct)
    {
        var res = await _service.CriarAsync(_user.TenantId, _user.UserId ?? Guid.Empty, User.Identity?.Name, GetSetorId(), vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação criada." : res.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "ADMIN,ADMINISTRADOROPHIR")]
    [HttpPost("{id:guid}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatus(Guid id, SolicitacaoUpdateStatusVM vm, CancellationToken ct)
    {
        var res = await _service.AtualizarStatusAsync(_user.TenantId, id, _user.UserId ?? Guid.Empty, User.Identity?.Name, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Status atualizado." : res.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    private Guid? GetSetorId()
    {
        var raw = User.Claims.FirstOrDefault(c => c.Type == "setor_id")?.Value;
        return Guid.TryParse(raw, out var setorId) ? setorId : null;
    }
}
