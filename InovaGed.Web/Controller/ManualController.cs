using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ManualController : Controller
{
    [HttpGet("/Manual")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Manual Operacional — InovaGED";
        ViewData["Subtitle"] = "Manual completo de operação do sistema: fluxos, responsabilidades, boas práticas e trilhas de auditoria";
        return View();
    }
}
