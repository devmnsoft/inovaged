using InovaGed.Application.Ged.Classification;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Web.Security;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.GedAccess)]
[Route("Ged")]
public sealed class GedClassificationController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGedClassificationSuggestionService _suggestionService;

    public GedClassificationController(ICurrentUser currentUser, IGedClassificationSuggestionService suggestionService)
    { _currentUser = currentUser; _suggestionService = suggestionService; }

    [HttpGet("ClassificationQueue")]
    public IActionResult Queue() => View("~/InovaGed.Web/Views/GedClassification/Queue.cshtml");

    [HttpPost("Classification/GenerateSuggestion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateSuggestion(Guid documentId, CancellationToken ct)
    {
        var suggestion = await _suggestionService.SuggestForDocumentAsync(_currentUser.TenantId, documentId, _currentUser.UserId, ct);
        return Json(new { success = true, item = suggestion });
    }
}
