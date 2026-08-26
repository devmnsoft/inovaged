using InovaGed.Application.Identity;
using InovaGed.Application.SmartGed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("SmartGed")]
public sealed class SmartGedController : Controller
{
    private readonly IDocumentIntelligenceService _intelligence; private readonly IDocumentClassificationSuggestionService _classifications; private readonly IDocumentRetentionSuggestionService _retentions; private readonly ISmartGedSearchService _search; private readonly ICurrentUser _user;
    public SmartGedController(IDocumentIntelligenceService intelligence, IDocumentClassificationSuggestionService classifications, IDocumentRetentionSuggestionService retentions, ISmartGedSearchService search, ICurrentUser user) { _intelligence=intelligence; _classifications=classifications; _retentions=retentions; _search=search; _user=user; }
    [HttpGet("")] public IActionResult Index()=>View();
    [HttpGet("ReviewQueue")] public async Task<IActionResult> ReviewQueue(CancellationToken ct)=>View(new SmartGedReviewQueue(await _classifications.ListPendingAsync(_user.TenantId,ct),await _retentions.ListPendingAsync(_user.TenantId,ct)));
    [HttpGet("Document/{documentId:guid}")] public async Task<IActionResult> Document(Guid documentId,CancellationToken ct)=>View(await _intelligence.GetAnalysisAsync(_user.TenantId,documentId,ct));
    [HttpPost("Analyze/{documentId:guid}")][ValidateAntiForgeryToken] public async Task<IActionResult> Analyze(Guid documentId,CancellationToken ct){await _intelligence.AnalyzeDocumentAsync(_user.TenantId,documentId,_user.UserId,ct);TempData["Success"]="Análise local concluída e encaminhada para revisão humana.";return RedirectToAction(nameof(Document),new{documentId});}
    [HttpPost("ClassificationSuggestion/{id:guid}/Accept")][ValidateAntiForgeryToken] public async Task<IActionResult> AcceptClassification(Guid id,string? notes,CancellationToken ct){await _classifications.AcceptAsync(_user.TenantId,id,_user.UserId,notes,ct);return RedirectToAction(nameof(ReviewQueue));}
    [HttpPost("ClassificationSuggestion/{id:guid}/Reject")][ValidateAntiForgeryToken] public async Task<IActionResult> RejectClassification(Guid id,string reason,CancellationToken ct){await _classifications.RejectAsync(_user.TenantId,id,_user.UserId,reason,ct);return RedirectToAction(nameof(ReviewQueue));}
    [HttpPost("RetentionSuggestion/{id:guid}/Accept")][ValidateAntiForgeryToken] public async Task<IActionResult> AcceptRetention(Guid id,string? notes,CancellationToken ct){await _retentions.AcceptAsync(_user.TenantId,id,_user.UserId,notes,ct);return RedirectToAction(nameof(ReviewQueue));}
    [HttpPost("RetentionSuggestion/{id:guid}/Reject")][ValidateAntiForgeryToken] public async Task<IActionResult> RejectRetention(Guid id,string reason,CancellationToken ct){await _retentions.RejectAsync(_user.TenantId,id,_user.UserId,reason,ct);return RedirectToAction(nameof(ReviewQueue));}
    [HttpGet("Quality")] public async Task<IActionResult> Quality(CancellationToken ct)=>View(await BuildQueue(ct));
    [HttpGet("Search")] public IActionResult Search()=>View(new SmartGedSearchResult("",[],0));
    [HttpPost("Search")][ValidateAntiForgeryToken] public async Task<IActionResult> Search(string query,CancellationToken ct)=>View(await _search.SearchAsync(new(_user.TenantId,_user.UserId,query),ct));
    private async Task<SmartGedReviewQueue> BuildQueue(CancellationToken ct)=>new(await _classifications.ListPendingAsync(_user.TenantId,ct),await _retentions.ListPendingAsync(_user.TenantId,ct));
}
