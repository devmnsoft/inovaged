using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ManualController : Controller
{
    [HttpGet("/Manual")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Manual da PoC";
        ViewData["Subtitle"] = "Roteiro guiado: o que é, como operar e o que demonstrar (itens 1–27)";
        return View();
    }
}