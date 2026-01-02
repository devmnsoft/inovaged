using InovaGed.Application.Classification;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.Classification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class ClassificationController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentClassificationQueries _queries;
    private readonly DocumentClassificationAppService _app;
    private readonly IDocumentTypeCatalogQueries _types;

    public ClassificationController(
        ICurrentUser currentUser,
        IDocumentClassificationQueries queries,
        DocumentClassificationAppService app,
        IDocumentTypeCatalogQueries types)
    {
        _currentUser = currentUser;
        _queries = queries;
        _app = app;
        _types = types;
    }

    [HttpGet]
    public async Task<IActionResult> Panel(Guid documentId, CancellationToken ct)
    {
        var dto = await _queries.GetAsync(_currentUser.TenantId, documentId, ct);
        return PartialView("_DocumentClassificationPanel", dto);
    }

    [HttpGet]
    public async Task<IActionResult> EditModal(Guid documentId, CancellationToken ct)
    {
        var dto = await _queries.GetAsync(_currentUser.TenantId, documentId, ct);
        var typeList = await _types.ListAsync(_currentUser.TenantId, ct);

        var vm = new EditClassificationVM
        {
            DocumentId = documentId,
            DocumentTypeId = dto?.DocumentTypeId,
            TagsCsv = dto?.Tags is { Count: > 0 } ? string.Join(", ", dto.Tags) : "",
            MetadataLines = dto?.Metadata is { Count: > 0 }
                ? string.Join("\n", dto.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))
                : "",
            AvailableTypes = typeList
                .OrderBy(x => x.Name)
                .Select(t => new EditClassificationVM.DocumentTypeItemVM
                {
                    Id = t.Id,
                    Name = t.Name
                })
                .ToList()
        };

        return PartialView("_EditClassificationModal", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManual(EditClassificationVM vm, CancellationToken ct)
    {
        await _app.SaveManualAsync(
            documentId: vm.DocumentId,
            documentTypeId: vm.DocumentTypeId,
            tagsCsv: vm.TagsCsv,
            metadataLines: vm.MetadataLines,
            ct: ct);

        return Ok();
    }

    // ✅ Aplicar sugestão (OCR / regras) como classificação manual
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplySuggestion([FromForm] Guid documentId, CancellationToken ct)
    {
        var dto = await _queries.GetAsync(_currentUser.TenantId, documentId, ct);
        if (dto?.SuggestedTypeId == null)
            return BadRequest("Não há sugestão para aplicar.");

        await _app.SaveManualAsync(
            documentId: documentId,
            documentTypeId: dto.SuggestedTypeId,
            tagsCsv: dto.Tags is { Count: > 0 } ? string.Join(", ", dto.Tags) : null,
            metadataLines: dto.Metadata is { Count: > 0 } ? string.Join("\n", dto.Metadata.Select(kv => $"{kv.Key}={kv.Value}")) : null,
            ct: ct);

        return Ok();
    }
}
