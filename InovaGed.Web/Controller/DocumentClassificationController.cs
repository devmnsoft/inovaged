using InovaGed.Application.Documents;
using InovaGed.Application.Retention;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Route("Classification")]
public sealed class DocumentClassificationController : Controller
{
    private readonly IDocumentCommands _docCmd;
    private readonly RetentionRecalcService _retention;
    private readonly ILogger<DocumentClassificationController> _logger;

    // ✅ Troque para seu contexto real
    private Guid TenantId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid UserId => Guid.Empty;

    public DocumentClassificationController(
        IDocumentCommands docCmd,
        RetentionRecalcService retention,
        ILogger<DocumentClassificationController> logger)
    {
        _docCmd = docCmd;
        _retention = retention;
        _logger = logger;
    }

    [HttpPost("Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid documentId, Guid classificationId, CancellationToken ct)
    {
        try
        {
            await _docCmd.ApplyClassificationAsync(TenantId, UserId, documentId, classificationId, ct);

            // ✅ Recalcula só esse documento
            await _retention.RunOneAsync(TenantId, documentId, dueSoonDays: 30, ct);

            TempData["Success"] = "Classificação aplicada e temporalidade atualizada.";
            return RedirectToAction("Details", "Ged", new { id = documentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply classification failed");
            TempData["Error"] = "Falha ao aplicar classificação.";
            return RedirectToAction("Details", "Ged", new { id = documentId });
        }
    }
}