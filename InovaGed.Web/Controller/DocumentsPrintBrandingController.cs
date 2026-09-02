using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("Documents/PrintBranding")]
public sealed class DocumentsPrintBrandingController(IDbConnectionFactory factory) : GedControllerBase(factory)
{
    [HttpGet("")] public IActionResult Index() => Redirect("/Labels/LogoLayout/DOCUMENT_PRINT_HEADER");
    [HttpPost("Save"),ValidateAntiForgeryToken] public IActionResult Save(LogoLayoutInput input) => RedirectPreserveMethod("/Labels/LogoLayout/DOCUMENT_PRINT_HEADER/Save");
    [HttpGet("TestPrint")] public IActionResult TestPrint() => Redirect("/Labels/LogoLayout/DOCUMENT_PRINT_HEADER/PrintTest");
}
