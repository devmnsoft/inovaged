using InovaGed.Application.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.Retention;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("DocumentClassification")]
public sealed class DocumentClassificationController : Controller
{
    private readonly IDocumentCommands _docCmd;
    private readonly RetentionRecalcService _retention;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DocumentClassificationController> _logger;

    public DocumentClassificationController(
        IDocumentCommands docCmd,
        RetentionRecalcService retention,
        ICurrentUser currentUser,
        ILogger<DocumentClassificationController> logger)
    {
        _docCmd = docCmd;
        _retention = retention;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(
        [FromForm] Guid documentId,
        [FromForm] Guid classificationId,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return RedirectToAction("Login", "Account");

        try
        {
            if (documentId == Guid.Empty)
            {
                TempData["Error"] = "Documento inválido.";
                return RedirectToAction("Index", "Ged");
            }

            if (classificationId == Guid.Empty)
            {
                TempData["Error"] = "Classificação inválida.";
                return RedirectToAction("Details", "Ged", new { id = documentId, openClassify = true });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            await _docCmd.ApplyClassificationAsync(
                tenantId,
                userId,
                documentId,
                classificationId,
                ct);

            await _retention.RunOneAsync(
                tenantId,
                documentId,
                dueSoonDays: 30,
                ct);

            TempData["Success"] = "Classificação aplicada e temporalidade recalculada.";

            return RedirectToAction("Details", "Ged", new
            {
                id = documentId,
                openClassify = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao aplicar classificação. DocumentId={DocumentId}, ClassificationId={ClassificationId}, UserId={UserId}",
                documentId,
                classificationId,
                _currentUser.UserId);

            TempData["Error"] = "Falha ao aplicar classificação.";

            return RedirectToAction("Details", "Ged", new
            {
                id = documentId,
                openClassify = true
            });
        }
    }
}