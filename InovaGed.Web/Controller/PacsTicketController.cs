using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using InovaGed.Application.Pacs; 
using InovaGed.Application.Identity;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Pacs")]
public sealed class PacsTicketController : Controller
{
    private readonly ILogger<PacsTicketController> _logger;
    private readonly IPacsIntegrationService _pacs;
    private readonly ICurrentUser _user; // sua abstração (TenantId/UserId)

    public PacsTicketController(
        ILogger<PacsTicketController> logger,
        IPacsIntegrationService pacs,
        ICurrentUser user)
    {
        _logger = logger;
        _pacs = pacs;
        _user = user;
    }

    [HttpGet("NovoChamado")]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        ViewBag.Folders = await _pacs.ListInboundFoldersAsync(ct);
        return View("New"); // Views/Pacs/New.cshtml
    }

    [HttpGet("Arquivos")]
    public async Task<IActionResult> Files([FromQuery] string folder, CancellationToken ct)
    {
        var files = await _pacs.ListInboundFilesAsync(folder, ct);
        return PartialView("_Files", files); // Views/Pacs/_Files.cshtml
    }

    [HttpPost("NovoChamado")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(NewPacsTicketVM vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Folders = await _pacs.ListInboundFoldersAsync(ct);
            return View("New", vm);
        }

        try
        {
            var tenantId = _user.TenantId;
            var userId = _user.UserId;

            var ticketId = await _pacs.CreateTicketFromFolderAsync(tenantId, userId, vm, ct);

            TempData["Ok"] = "Chamado criado e arquivos importados. OCR enfileirado (quando aplicável).";
            return RedirectToAction("Details", "Tickets", new { id = ticketId }); // ajuste para sua rota real
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar chamado via PACS. Folder={Folder}", vm.FolderName);
            ModelState.AddModelError("", ex.Message);

            ViewBag.Folders = await _pacs.ListInboundFoldersAsync(ct);
            return View("New", vm);
        }
    }
}