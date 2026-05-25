using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ManualController : Controller
{
    private readonly ILogger<ManualController> _logger;

    public ManualController(ILogger<ManualController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/Manual")]
    public IActionResult Index()
    {
        if (User.IsInRole(AppRoles.AdministradorOphir) || User.IsInRole(AppRoles.ArquivistaOphir))
        {
            _logger.LogWarning("Acesso ao manual bloqueado para perfil Ophir. User={User}", User?.Identity?.Name ?? "anonymous");
            return Redirect("/HospitalDocuments");
        }
        ViewData["Title"] = "Manual Operacional — InovaGED";
        ViewData["Subtitle"] = "Manual completo de operação do sistema: fluxos, responsabilidades, boas práticas e trilhas de auditoria";
        return View();
    }
}
