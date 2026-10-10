using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Application.SmartGed;
using InovaGed.Web.Models.SmartGed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("SmartGed")]
public sealed class SmartGedController : Controller
{
    private readonly IDocumentIntelligenceService _intelligence;
    private readonly IDocumentClassificationSuggestionService _classifications;
    private readonly IDocumentRetentionSuggestionService _retentions;
    private readonly ISmartGedSearchService _search;
    private readonly ICurrentUser _user;
    private readonly IDbConnectionFactory _db;
    private readonly InovaGed.Application.Security.IAbacAuthorizationService _authorization;

    public SmartGedController(
        IDocumentIntelligenceService intelligence,
        IDocumentClassificationSuggestionService classifications,
        IDocumentRetentionSuggestionService retentions,
        ISmartGedSearchService search,
        ICurrentUser user,
        IDbConnectionFactory db,
        InovaGed.Application.Security.IAbacAuthorizationService authorization)
    {
        _intelligence = intelligence;
        _classifications = classifications;
        _retentions = retentions;
        _search = search;
        _user = user;
        _db = db;
        _authorization = authorization;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("ReviewQueue")]
    public async Task<IActionResult> ReviewQueue([FromQuery] string? status = "PENDING", CancellationToken ct = default)
    {
        var st = string.IsNullOrWhiteSpace(status) ? "PENDING" : status.Trim().ToUpperInvariant();
        var classifications = await _classifications.ListAsync(_user.TenantId, st, ct);
        var retentions = await _retentions.ListAsync(_user.TenantId, st, ct);
        return View(new SmartGedReviewQueue(classifications, retentions, st));
    }

    [HttpGet("Document/{documentId:guid}")]
    public async Task<IActionResult> Document(Guid documentId, CancellationToken ct)
    {
        var allowed = await _authorization.CanAccessDocumentAsync(_user.TenantId, _user.UserId, documentId, "VIEW", new Dictionary<string, string>(), ct);
        if (!allowed) return Forbid();

        await using var conn = await _db.OpenAsync(ct);
        var doc = await conn.QuerySingleOrDefaultAsync<DocumentDetailsRow>(new CommandDefinition("""
select d.id as Id,
       coalesce(d.title, 'Documento') as Title,
       d.description as Description,
       d.current_version_id as CurrentVersionId,
       coalesce(v.version_number, 1) as VersionNumber,
       coalesce(pvi.code || ' - ' || coalesce(pvi.name, pvi.title), cp.code || ' - ' || cp.name) as CurrentClassification,
       d.classification_id as CurrentClassificationId,
       d.retention_status as CurrentRetentionStatus,
       v.ocr_status as OcrStatus,
       v.ocr_text as OcrText
from ged.document d
left join ged.document_version v on v.id = d.current_version_id
left join ged.classification_plan cp on cp.id = d.classification_id and cp.tenant_id = d.tenant_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id = d.tenant_id and pvi.version_id = d.classification_version_id and pvi.classification_id = d.classification_id
where d.tenant_id = @tenantId and d.id = @documentId and coalesce(d.reg_status, 'A') = 'A'
""", new { tenantId = _user.TenantId, documentId }, cancellationToken: ct));

        if (doc is null) return NotFound();

        var canClassify = await _authorization.CanAccessDocumentAsync(_user.TenantId, _user.UserId, documentId, "CLASSIFY", new Dictionary<string, string>(), ct);
        var canEdit = await _authorization.CanAccessDocumentAsync(_user.TenantId, _user.UserId, documentId, "EDIT", new Dictionary<string, string>(), ct);
        var intelligence = await _intelligence.GetAnalysisAsync(_user.TenantId, documentId, ct);

        var vm = new SmartGedDocumentVM
        {
            DocumentId = doc.Id,
            Title = doc.Title,
            Description = doc.Description,
            CurrentVersionId = doc.CurrentVersionId,
            VersionNumber = doc.VersionNumber,
            CurrentClassification = doc.CurrentClassification,
            CurrentClassificationId = doc.CurrentClassificationId,
            CurrentRetentionStatus = doc.CurrentRetentionStatus,
            OcrStatus = doc.OcrStatus,
            OcrText = doc.OcrText,
            OcrPending = string.IsNullOrWhiteSpace(doc.OcrText) || doc.OcrStatus is "PENDING" or "PROCESSING",
            Intelligence = intelligence,
            CanClassify = canClassify,
            CanEdit = canEdit
        };

        return View(vm);
    }

    [HttpPost("Analyze/{documentId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(Guid documentId, CancellationToken ct)
    {
        await _intelligence.AnalyzeDocumentAsync(_user.TenantId, documentId, _user.UserId, ct);
        TempData["Success"] = "Análise local concluída e encaminhada para revisão humana.";
        return RedirectToAction(nameof(Document), new { documentId });
    }

    [HttpPost("ClassificationSuggestion/{id:guid}/Accept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptClassification(Guid id, string? notes, CancellationToken ct)
    {
        await _classifications.AcceptAsync(_user.TenantId, id, _user.UserId, notes, ct);
        return RedirectToAction(nameof(ReviewQueue));
    }

    [HttpPost("ClassificationSuggestion/{id:guid}/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectClassification(Guid id, string reason, CancellationToken ct)
    {
        await _classifications.RejectAsync(_user.TenantId, id, _user.UserId, reason, ct);
        return RedirectToAction(nameof(ReviewQueue));
    }

    [HttpPost("RetentionSuggestion/{id:guid}/Accept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptRetention(Guid id, string? notes, CancellationToken ct)
    {
        await _retentions.AcceptAsync(_user.TenantId, id, _user.UserId, notes, ct);
        return RedirectToAction(nameof(ReviewQueue));
    }

    [HttpPost("RetentionSuggestion/{id:guid}/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectRetention(Guid id, string reason, CancellationToken ct)
    {
        await _retentions.RejectAsync(_user.TenantId, id, _user.UserId, reason, ct);
        return RedirectToAction(nameof(ReviewQueue));
    }

    [HttpGet("Quality")]
    public async Task<IActionResult> Quality(CancellationToken ct) => View(await BuildQueue(ct));

    [HttpGet("Search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] Guid? folderId,
        [FromQuery] Guid? classificationId,
        [FromQuery] Guid? typeId,
        [FromQuery] DateTime? createdAfter,
        [FromQuery] DateTime? createdBefore,
        [FromQuery] string? retentionStatus,
        CancellationToken ct)
    {
        var vm = new SmartGedSearchPageVM
        {
            Query = query,
            FolderId = folderId,
            ClassificationId = classificationId,
            TypeId = typeId,
            CreatedAfter = createdAfter,
            CreatedBefore = createdBefore,
            RetentionStatus = retentionStatus
        };
        await PopulateCombosAsync(vm, ct);

        if (!string.IsNullOrWhiteSpace(query) || folderId.HasValue || classificationId.HasValue || typeId.HasValue || createdAfter.HasValue || createdBefore.HasValue || !string.IsNullOrWhiteSpace(retentionStatus))
        {
            var q = new SmartGedSearchQuery(
                TenantId: _user.TenantId,
                UserId: _user.UserId,
                Text: query ?? string.Empty,
                Limit: 50,
                FolderId: folderId,
                ClassificationId: classificationId,
                TypeId: typeId,
                CreatedAfter: createdAfter,
                CreatedBefore: createdBefore,
                RetentionStatus: retentionStatus
            );
            vm.Result = await _search.SearchAsync(q, ct);
        }

        return View(vm);
    }

    [HttpPost("Search")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Search(SmartGedSearchPageVM vm, CancellationToken ct)
    {
        await PopulateCombosAsync(vm, ct);
        var q = new SmartGedSearchQuery(
            TenantId: _user.TenantId,
            UserId: _user.UserId,
            Text: vm.Query ?? string.Empty,
            Limit: 50,
            FolderId: vm.FolderId,
            ClassificationId: vm.ClassificationId,
            TypeId: vm.TypeId,
            CreatedAfter: vm.CreatedAfter,
            CreatedBefore: vm.CreatedBefore,
            RetentionStatus: vm.RetentionStatus
        );
        vm.Result = await _search.SearchAsync(q, ct);
        return View(vm);
    }

    private async Task PopulateCombosAsync(SmartGedSearchPageVM vm, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var folders = await conn.QueryAsync<(Guid Id, string Name)>(
            new CommandDefinition("select id, name from ged.folder where tenant_id=@TenantId and coalesce(reg_status,'A')='A' order by name", new { TenantId = _user.TenantId }, cancellationToken: ct));
        vm.Folders = folders.Select(f => new SelectListItem(f.Name, f.Id.ToString(), f.Id == vm.FolderId)).ToList();

        var classes = await conn.QueryAsync<(Guid Id, string Code, string Name)>(
            new CommandDefinition("""
select coalesce(i.classification_id, cp.id) as Id,
       coalesce(i.code, cp.code) as Code,
       coalesce(i.name, cp.name, i.title) as Name
from ged.classification_plan_version_item i
join ged.classification_plan_version v on v.tenant_id=i.tenant_id and v.id=i.version_id
left join ged.classification_plan cp on cp.tenant_id=i.tenant_id and cp.id=i.classification_id
where i.tenant_id=@TenantId and coalesce(i.is_active,true) and coalesce(v.reg_status,'A')='A'
  and v.version_no=(select max(version_no) from ged.classification_plan_version where tenant_id=@TenantId and coalesce(reg_status,'A')='A')
order by i.code;
""", new { TenantId = _user.TenantId }, cancellationToken: ct));
        vm.Classifications = classes.Select(c => new SelectListItem($"{c.Code} - {c.Name}", c.Id.ToString(), c.Id == vm.ClassificationId)).ToList();

        var types = await conn.QueryAsync<(Guid Id, string Name)>(
            new CommandDefinition("select id, name from ged.document_type where tenant_id=@TenantId and coalesce(reg_status,'A')='A' order by name", new { TenantId = _user.TenantId }, cancellationToken: ct));
        vm.DocumentTypes = types.Select(t => new SelectListItem(t.Name, t.Id.ToString(), t.Id == vm.TypeId)).ToList();
    }

    private async Task<SmartGedReviewQueue> BuildQueue(CancellationToken ct) =>
        new(await _classifications.ListPendingAsync(_user.TenantId, ct), await _retentions.ListPendingAsync(_user.TenantId, ct));

    private sealed class DocumentDetailsRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public int VersionNumber { get; set; } = 1;
        public string? CurrentClassification { get; set; }
        public Guid? CurrentClassificationId { get; set; }
        public string? CurrentRetentionStatus { get; set; }
        public string? OcrStatus { get; set; }
        public string? OcrText { get; set; }
    }
}
