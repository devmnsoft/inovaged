using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace InovaGed.Web.Controllers;
[Authorize,Route("ReleaseReadiness")]
public sealed class ReleaseReadinessController:Controller
{
 [HttpGet("")] public IActionResult Index()=>RedirectToAction("Readiness","Administration");
}
