using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class AuditController : Controller
{
    private readonly ILogger<AuditController> _logger;
    private readonly ICurrentUser _user;
    private readonly IAuditQueries _queries;

    public AuditController(ILogger<AuditController> logger, ICurrentUser user, IAuditQueries queries)
    {
        _logger = logger;
        _user = user;
        _queries = queries;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] AuditSearchVM search, CancellationToken ct)
    {
        try
        {
            ViewData["Title"] = "Auditoria";
            ViewData["Subtitle"] = "Log de Auditoria";

            var vm = await _queries.ListAsync(_user.TenantId, search, ct);
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit.Index failed");
            TempData["Err"] = "Erro ao carregar auditoria.";
            return View(new AuditIndexVM());
        }
    }

    [HttpGet("AccessDenied")]
    public async Task<IActionResult> AccessDenied([FromQuery] AuditSearchVM search, CancellationToken ct)
    {
        try
        {
            ViewData["Title"] = "Falhas de Acesso";
            ViewData["Subtitle"] = "Auditoria";

            search.EventType ??= "ACCESS_DENIED";
            search.Action ??= "ACCESS_DENIED";

            var vm = await _queries.ListAccessDeniedAsync(_user.TenantId, search, ct);
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit.AccessDenied failed");
            TempData["Err"] = "Erro ao carregar falhas de acesso.";
            return View(new AuditIndexVM());
        }
    }


}