using System.Text.Json;
using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Poc")]
public sealed class PocController : Controller
{
    private readonly IPocCatalogService _catalog;
    private readonly IAppAuditLogService _audit;
    private readonly ICurrentUser _currentUser;

    public PocController(IPocCatalogService catalog, IAppAuditLogService audit, ICurrentUser currentUser)
    {
        _catalog = catalog;
        _audit = audit;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    public IActionResult Index() => View(_catalog.Dashboard());

    [HttpGet("Checklist")]
    public IActionResult Checklist() => View(_catalog.Checklist());

    [HttpGet("Demo")]
    public IActionResult Demo() => View(_catalog.Demo());

    [HttpGet("Evidences")]
    public IActionResult Evidences() => View(_catalog.Evidences());

    [HttpGet("Evidences/Manifest")]
    public IActionResult EvidenceManifest()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            product = "InovaGED",
            generatedAt = DateTimeOffset.UtcNow,
            correlationId = HttpContext.TraceIdentifier,
            evidences = _catalog.Evidences().Items
        }, new JsonSerializerOptions { WriteIndented = true });
        return File(payload, "application/json", $"inovaged-poc-evidencias-{DateTime.UtcNow:yyyyMMddHHmm}.json");
    }

    [HttpPost("Validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateModule(string moduleKey, CancellationToken ct)
    {
        if (!_catalog.Validate(moduleKey, DateTimeOffset.UtcNow))
            return NotFound(new { success = false, message = "Módulo não encontrado.", correlationId = HttpContext.TraceIdentifier });

        await _audit.LogBusinessAsync(_currentUser.TenantId, _currentUser.UserId, "VIEW", "poc_module", null,
            "Módulo da Central PoC validado.", new { moduleKey, correlationId = HttpContext.TraceIdentifier }, moduleKey, ct);
        return Ok(new { success = true, message = "Validação registrada com evidência de auditoria.", validatedAt = DateTimeOffset.UtcNow, correlationId = HttpContext.TraceIdentifier });
    }
}
