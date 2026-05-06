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
        ViewData["Subtitle"] = "Guia interativo de uso do GED, Protocolo, OCR, Classificação, Assinatura, Guarda, Temporalidade e Auditoria";
        return View();
    }
}
