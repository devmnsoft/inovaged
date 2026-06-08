using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Roles = AppRoles.Admin + "," + AppRoles.ArquivistaOphir)]
[Route("ProtocolRequests")]
public sealed class ProtocolRequestsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => RedirectToAction("Index", "Protocolo", new { visao = "enviados" });

    [HttpGet("New")]
    public IActionResult New() => RedirectToAction("Novo", "Protocolo");
}
