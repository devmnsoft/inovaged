using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

public  class HomeController : Controller
{
    public IActionResult Index()
        => RedirectToAction("Index", "Ged"); // manda direto pro GED
}
