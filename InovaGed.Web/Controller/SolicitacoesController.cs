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
    private readonly ILogger<SolicitacoesController> _logger;

    public SolicitacoesController(ISolicitacaoService service, ICurrentUser user, ILogger<SolicitacoesController> logger)
    {
        _service = service;
        _user = user;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            // ADMIN possui visão global das solicitações.
            var isAdmin = User.IsInRole(AppRoles.Admin);
            var setorId = GetSetorId();
            var userId = _user.UserId != Guid.Empty ? _user.UserId : (Guid?)null;

            var rows = await _service.ListarParaUsuarioAsync(_user.TenantId, userId ?? Guid.Empty, setorId, isAdmin, ct);
            ViewBag.PendentesCount = isAdmin ? await _service.PendentesCountAsync(_user.TenantId, ct) : 0;

            return View(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar página de solicitações para usuário {UserId}, Tenant {TenantId}", _user.UserId, _user.TenantId);
            TempData["Err"] = "Erro ao carregar solicitações.";
            return View(new List<object>());
        }
    }

    [HttpGet("Historico")]
    public async Task<IActionResult> Historico(CancellationToken ct)
    {
        try
        {
            // ADMIN possui visão global do histórico.
            var isAdmin = User.IsInRole(AppRoles.Admin);
            var userId = _user.UserId != Guid.Empty ? _user.UserId : (Guid?)null;
            var rows = await _service.HistoricoAsync(_user.TenantId, userId, GetSetorId(), isAdmin, ct);

            return View("HistoricoPedidos", rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar histórico de solicitações para usuário {UserId}, Tenant {TenantId}", _user.UserId, _user.TenantId);
            TempData["Err"] = "Erro ao carregar histórico.";
            return View("HistoricoPedidos", new List<object>());
        }
    }

    [HttpPost("Criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(SolicitacaoCreateVM vm, CancellationToken ct)
    {
        try
        {
            if (_user.UserId == Guid.Empty)
            {
                TempData["Err"] = "Usuário inválido para criar solicitação.";
                return RedirectToAction(nameof(Index));
            }
            var userId = _user.UserId;
            var usuarioNome = User.Identity?.Name;

            var res = await _service.CriarAsync(_user.TenantId, userId, usuarioNome, GetSetorId(), vm, ct);

            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação criada." : res.ErrorMessage;

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar solicitação para usuário {UserId}, Tenant {TenantId}", _user.UserId, _user.TenantId);
            TempData["Err"] = "Erro ao criar solicitação.";
            return RedirectToAction(nameof(Index));
        }
    }

    // Feedback administrativo exclusivo para ADMIN global.
    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("{id:guid}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatus(Guid id, SolicitacaoUpdateStatusVM vm, CancellationToken ct)
    {
        try
        {
            if (_user.UserId == Guid.Empty)
            {
                TempData["Err"] = "Administrador inválido.";
                return RedirectToAction(nameof(Index));
            }
            var adminId = _user.UserId;
            var adminNome = User.Identity?.Name;

            var res = await _service.AtualizarStatusAsync(_user.TenantId, id, adminId, adminNome, vm, ct);

            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Status atualizado." : res.ErrorMessage;

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar status da solicitação {SolicitacaoId}, Tenant {TenantId}", id, _user.TenantId);
            TempData["Err"] = "Erro ao atualizar status.";
            return RedirectToAction(nameof(Index));
        }
    }



    [Authorize(Roles = AppRoles.Admin)]
    [HttpPost("ExcluirAntigas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirAntigas(int dias = 90, CancellationToken ct = default)
    {
        try
        {
            if (_user.UserId == Guid.Empty)
            {
                TempData["Err"] = "Administrador inválido.";
                return RedirectToAction(nameof(Index));
            }
            var adminId = _user.UserId;
            var adminNome = User.Identity?.Name;
            var limite = DateTime.UtcNow.AddDays(-Math.Max(1, dias));
            var res = await _service.ExcluirAntigasAsync(_user.TenantId, adminId, adminNome, limite, ct);
            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitações antigas removidas." : res.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir solicitações antigas. Tenant {TenantId}", _user.TenantId);
            TempData["Err"] = "Erro ao excluir solicitações antigas.";
            return RedirectToAction(nameof(Index));
        }
    }

    private Guid? GetSetorId()
    {
        try
        {
            var raw = User.Claims.FirstOrDefault(c => c.Type == "setor_id")?.Value;
            return Guid.TryParse(raw, out var setorId) ? setorId : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao recuperar setor do usuário {UserId}", _user.UserId);
            return null;
        }
    }
}
