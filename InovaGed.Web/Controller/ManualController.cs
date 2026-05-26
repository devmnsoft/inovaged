using InovaGed.Application.Security;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ManualController : Controller
{
    private readonly ILogger<ManualController> _logger;
    private readonly IGedAccessPolicyService _accessPolicy;

    public ManualController(ILogger<ManualController> logger, IGedAccessPolicyService accessPolicy)
    {
        _logger = logger;
        _accessPolicy = accessPolicy;
    }

    [HttpGet("/Manual")]
    public IActionResult Index()
    {
        if (_accessPolicy.IsAdministradorOphir(User) || _accessPolicy.IsArquivistaOphir(User))
        {
            _logger.LogWarning("Acesso ao manual bloqueado para perfil Ophir. User={User}", User?.Identity?.Name ?? "anonymous");
            return Redirect("/HospitalDocuments");
        }
        ViewData["Title"] = "Manual Operacional — InovaGED";
        ViewData["Subtitle"] = "Manual completo de operação do sistema: fluxos, responsabilidades, boas práticas e trilhas de auditoria";
        return View();
    }
}
