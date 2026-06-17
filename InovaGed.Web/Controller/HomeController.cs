using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        var isFullAdmin = RolePolicyHelper.IsFullAdmin(User);
        var target = AppStartRouteResolver.GetDefaultHome(User);

        _logger.LogInformation(
            "Home redirect resolvido. FullAdmin={FullAdmin} Target={Target} User={User}",
            isFullAdmin,
            target,
            User.Identity?.Name ?? "anonymous");

        return Redirect(target);
    }

    [AllowAnonymous]
    [Route("Home/Error")]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        ViewBag.RequestId = HttpContext.TraceIdentifier;
        ViewBag.ErrorPath = feature?.Path;
        return View();
    }

    [AllowAnonymous]
    [Route("Home/Status/{statusCode:int}")]
    public IActionResult Status(int statusCode)
    {
        ViewBag.StatusCode = statusCode;
        return View("Status");
    }
}
