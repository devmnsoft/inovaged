using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive2;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

/// <summary>
/// Bloco B: trânsito físico de prontuários entre o arquivo central (DAME) e os setores hospitalares.
/// Visão para Admin/Administrador/AdministradorOphir/ArquivistaOphir; ações de escrita exigem PhysicalTransitManage.
/// </summary>
[Authorize(Policy = AppPolicies.PhysicalTransitView)]
[Route("[controller]")]
public sealed class PhysicalTransitController : GedControllerBase
{
    private readonly ILogger<PhysicalTransitController> _logger;
    private readonly IPhysicalTransitService _transit;

    public PhysicalTransitController(ILogger<PhysicalTransitController> logger, IDbConnectionFactory dbFactory, IPhysicalTransitService transit) : base(dbFactory)
    {
        _logger = logger;
        _transit = transit;
    }

    private bool CanManage => AppMenuPolicy.CanManagePhysicalTransit(User);

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(Guid? sectorId, string? status, string? q, CancellationToken ct)
    {
        var result = await _transit.BatchesAsync(TenantId, sectorId, status, q, ct);
        ViewBag.Sectors = await _transit.SectorsAsync(TenantId, ct);
        ViewBag.Counts = result.Counts;
        ViewBag.SectorId = sectorId;
        ViewBag.Status = status;
        ViewBag.Q = q;
        ViewBag.CanManage = CanManage;
        return View(result.Rows);
    }

    [HttpGet("Checkout")]
    public async Task<IActionResult> Checkout(string? q, CancellationToken ct)
    {
        if (!CanManage) return Forbid();
        ViewBag.Sectors = await _transit.SectorsAsync(TenantId, ct);
        ViewBag.Catalog = await _transit.CatalogAsync(TenantId, q, ct);
        ViewBag.Q = q;
        return View();
    }

    [HttpPost("Checkout"), ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.PhysicalTransitManage)]
    public async Task<IActionResult> Checkout(Guid sectorId, string? carrierName, string? carrierId, string? reason, string? dueAt, Guid[] documentIds, CancellationToken ct)
    {
        var sectors = await _transit.SectorsAsync(TenantId, ct);
        var sector = sectors.FirstOrDefault(s => s.Id == sectorId);
        DateTimeOffset? due = null;
        if (DateTime.TryParse(dueAt, out var dt))
        {
            // O formulário envia hora local sem offset; o Npgsql exige UTC para timestamptz.
            var asUtc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
            due = new DateTimeOffset(asUtc);
        }
        try
        {
            var result = await _transit.CheckoutAsync(TenantId, sector?.Id, sector?.Name, carrierName, carrierId, reason, documentIds ?? Array.Empty<Guid>(), UserId, UserNameSafe, due, ct);
            if (result.AcceptedCount == 0)
            {
                TempData["Err"] = "Nenhum prontuário saiu. " + string.Join(" ", result.Rejected.Take(5).Select(r => $"{r.DocCode}: {r.Reason}"));
                return RedirectToAction(nameof(Checkout));
            }
            TempData["Ok"] = $"Saída registrada: lote {result.BatchNumber} com {result.AcceptedCount} prontuário(s).";
            if (result.Rejected.Count > 0)
                TempData["Warn"] = $"{result.Rejected.Count} item(ns) não saíram: {string.Join("; ", result.Rejected.Take(5).Select(r => r.DocCode + " — " + r.Reason))}{(result.Rejected.Count > 5 ? " …" : "")}";
            return RedirectToAction(nameof(BatchDetail), new { id = result.BatchId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Err"] = ex.Message;
            return RedirectToAction(nameof(Checkout));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar saída física (tenant={Tenant}).", TenantId);
            TempData["Err"] = "Não foi possível registrar a saída agora. Tente novamente.";
            return RedirectToAction(nameof(Checkout));
        }
    }

    [HttpGet("Batch/{id:guid}")]
    public async Task<IActionResult> BatchDetail(Guid id, CancellationToken ct)
    {
        var model = await _transit.BatchDetailAsync(TenantId, id, ct);
        if (model is null) return NotFound();
        ViewBag.CanManage = CanManage;
        return View(model);
    }

    [HttpPost("BatchReturn"), ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.PhysicalTransitManage)]
    public async Task<IActionResult> BatchReturn(Guid id, Guid[] loanIds, string? notes, CancellationToken ct)
    {
        try
        {
            var ok = await _transit.CheckinAsync(TenantId, id, loanIds ?? Array.Empty<Guid>(), notes, UserId, UserNameSafe, ct);
            TempData[ok ? "Ok" : "Err"] = ok ? "Devolução registrada com sucesso." : "Lote não encontrado neste ambiente.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Err"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar devolução (lote={Id}, tenant={Tenant}).", id, TenantId);
            TempData["Err"] = "Não foi possível registrar a devolução agora. Tente novamente.";
        }
        return RedirectToAction(nameof(BatchDetail), new { id });
    }

    [HttpPost("MarkLost"), ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.PhysicalTransitManage)]
    public async Task<IActionResult> MarkLost(Guid id, string? justification, CancellationToken ct)
    {
        try
        {
            var r = await _transit.MarkLostAsync(TenantId, id, justification, UserId, UserNameSafe, ct);
            TempData[r.Ok ? "Ok" : "Err"] = r.Ok ? "Extravio registrado com justificativa e trilha de custódia." : (r.Error ?? "Não foi possível registrar o extravio.");
            if (r.BatchId is not null) return RedirectToAction(nameof(BatchDetail), new { id = r.BatchId.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar extravio (registro={Id}, tenant={Tenant}).", id, TenantId);
            TempData["Err"] = "Não foi possível registrar o extravio agora. Tente novamente.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Batch/{id:guid}/Cautela")]
    public async Task<IActionResult> Cautela(Guid id, CancellationToken ct)
    {
        var model = await _transit.BatchDetailAsync(TenantId, id, ct);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpGet("History/{documentId:guid}")]
    public async Task<IActionResult> History(Guid documentId, CancellationToken ct)
    {
        var model = await _transit.DocumentHistoryAsync(TenantId, documentId, ct);
        if (model.Doc is null && model.Events.Count == 0) return NotFound();
        return View(model);
    }
}
