using Microsoft.AspNetCore.Diagnostics;
using InovaGed.Web.Routing;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

public  class HomeController : Controller
{
    public IActionResult Index()
    {
        // Página inicial padrão do sistema: busca de documentos hospitalares.
        // Não redirecionar para /Ged, pois perfis Ophir/Hospital não devem iniciar no GED administrativo.
        return Redirect(AppDefaultRoutes.HospitalDocuments);
    }

    [Route("Home/Error")]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        ViewBag.RequestId = HttpContext.TraceIdentifier;
        ViewBag.ErrorPath = feature?.Path;
        return View();
    }

    [Route("Home/Status/{statusCode:int}")]
    public IActionResult Status(int statusCode)
    {
        ViewBag.StatusCode = statusCode;
        return View("Status");
    }
}
