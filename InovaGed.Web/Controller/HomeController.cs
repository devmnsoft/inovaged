using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

public  class HomeController : Controller
{
    public IActionResult Index()
        => RedirectToAction("Index", "Ged"); // manda direto pro GED

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
