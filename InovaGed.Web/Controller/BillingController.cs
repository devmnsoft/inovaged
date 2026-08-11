using InovaGed.Application.Billing;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using InovaGed.Web.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("Billing")]
public sealed class BillingController(ICurrentUser user, IBillingQueries queries, IBillingExtractionService extraction, IBillingRuleService rules) : Controller
{
    [HttpGet("")]
    [HttpGet("Documents")]
    public async Task<IActionResult> Index(string? supplier, string? supplierDocument, string? competence, string? status, decimal? minimumAmount, CancellationToken ct)
    {
        var data = await queries.DashboardAsync(user.TenantId, new(supplier, supplierDocument, competence, status, minimumAmount), ct);
        ViewBag.Kpis = data.Kpis; ViewBag.Filter = new BillingFilter(supplier, supplierDocument, competence, status, minimumAmount);
        return View(data.Rows);
    }

    [HttpGet("Extraction")]
    public IActionResult Extraction() => View();

    [HttpPost("Extraction")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extraction(Guid documentId, CancellationToken ct)
    {
        if (documentId == Guid.Empty) { TempData["Err"] = "Selecione um documento válido."; return View(); }
        var result = await extraction.ExtractDocumentAsync(user.TenantId, documentId, ct);
        if (result is null) { TempData["Err"] = "O documento não possui texto OCR ou não apresenta conteúdo fiscal/financeiro reconhecível."; return View(); }
        TempData["Ok"] = "Extração concluída. Revise os campos antes da aprovação.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct) => await queries.GetAsync(user.TenantId, id, ct) is { } item ? View(item) : NotFound();

    [HttpGet("Review/{id:guid}")]
    public async Task<IActionResult> Review(Guid id, CancellationToken ct) => await queries.GetAsync(user.TenantId, id, ct) is { } item ? View(ToReviewInput(item)) : NotFound();

    [HttpPost("Review/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(Guid id, BillingReviewInput input, [FromServices] IBillingCommands commands, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var existing = await queries.GetAsync(user.TenantId, id, ct);
            if (existing is null) return NotFound();
            input.Id = id; input.DocumentTitle = existing.DocumentTitle;
            return View(input);
        }
        var ok = await commands.ReviewAsync(user.TenantId, id, user.UserId, input, ct);
        TempData[ok ? "Ok" : "Err"] = ok ? "Revisão registrada com usuário, data e status." : "Extração não encontrada para este tenant.";
        return RedirectToAction(ok ? nameof(Details) : nameof(Index), ok ? new { id } : null);
    }

    [HttpGet("Rules")]
    public async Task<IActionResult> Rules(Guid? edit, CancellationToken ct)
    {
        var items = await rules.ListAsync(user.TenantId, ct);
        var form = edit.HasValue ? items.FirstOrDefault(x => x.Id == edit.Value) ?? new BillingExtractionRuleInput() : new BillingExtractionRuleInput();
        return View(new BillingRulesPageVm(items, form));
    }

    [HttpPost("Rules")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRule(BillingExtractionRuleInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Rules", new BillingRulesPageVm(await rules.ListAsync(user.TenantId, ct), input));
        await rules.SaveAsync(user.TenantId, user.UserId, input, ct);
        TempData["Ok"] = "Regra de extração salva e disponível para os próximos OCRs.";
        return RedirectToAction(nameof(Rules));
    }

    [HttpPost("Rules/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        TempData[await rules.DeleteAsync(user.TenantId, user.UserId, id, ct) ? "Ok" : "Err"] = "Regra removida.";
        return RedirectToAction(nameof(Rules));
    }

    private static BillingReviewInput ToReviewInput(BillingExtractionDto item) => new()
    {
        Id = item.Id, DocumentTitle = item.DocumentTitle, SupplierName = item.SupplierName, SupplierDocument = item.SupplierDocument,
        InvoiceNumber = item.InvoiceNumber, InvoiceSeries = item.InvoiceSeries, IssueDate = item.IssueDate, DueDate = item.DueDate,
        CompetenceMonth = item.CompetenceMonth, GrossAmount = item.GrossAmount, NetAmount = item.NetAmount, TaxAmount = item.TaxAmount,
        ContractNumber = item.ContractNumber, PurchaseOrder = item.PurchaseOrder, CostCenter = item.CostCenter,
        ServiceDescription = item.ServiceDescription, UstQuantity = item.UstQuantity, UstUnitValue = item.UstUnitValue
    };
}
