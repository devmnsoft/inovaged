using InovaGed.Application.Billing;
using InovaGed.Application.Security;
using System.Text.Json;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Filters;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using InovaGed.Web.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Billing")]
public sealed class BillingController(ICurrentUser user, IBillingQueries queries, IBillingExtractionService extraction, IBillingRuleService rules, IBillingOperations operations, IPermissionService permissions) : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor.RouteValues["action"];
        var code = action switch
        {
            "Review" => "BILLING_REVIEW",
            "Rules" or "SaveRule" or "DeleteRule" or "Extraction" or "SaveDuplicatePolicy" => "BILLING_MANAGE",
            "AddInstallment" or "Settle" or "Reconcile" => "BILLING_SETTLE",
            _ => "BILLING_VIEW"
        };
        if (!RolePolicyHelper.IsFullAdmin(User) && !await permissions.HasAsync(user.TenantId, user.UserId, code, HttpContext.RequestAborted))
        { context.Result = Forbid(); return; }
        await next();
    }

    [HttpGet("")]
    [HttpGet("Documents")]
    public async Task<IActionResult> Index(string? supplier, string? supplierDocument, string? competence, string? status, [ModelBinder(BinderType = typeof(InovaGed.Web.ModelBinding.BillingDecimalModelBinder))] decimal? minimumAmount, CancellationToken ct, int page = 1, int pageSize = 50, Guid? responsibleUserId = null, bool? overdue = null)
    {
        var filter = new BillingFilter(supplier, supplierDocument, competence, status, minimumAmount, Math.Max(1,page), Math.Clamp(pageSize,1,200), responsibleUserId, overdue);
        var data = await queries.DashboardAsync(user.TenantId, filter, ct);
        ViewBag.Kpis = data.Kpis; ViewBag.Filter = filter;
        ViewBag.Assignees = await operations.AssigneesAsync(user.TenantId, ct);
        return View(data.Rows);
    }

    [HttpGet("Checklist")]
    public async Task<IActionResult> Checklist(CancellationToken ct, int page = 1)
    {
        var data = await queries.DashboardAsync(user.TenantId, new BillingFilter(Page: Math.Max(1,page)), ct);
        ViewBag.Page = Math.Max(1,page); ViewBag.Total = data.Kpis.Extracted;
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
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var item = await queries.GetAsync(user.TenantId, id, ct);
        if (item is null) return NotFound();
        ViewBag.Operations = await operations.GetAsync(user.TenantId, id, ct);
        return View(item);
    }

    [HttpGet("Review/{id:guid}")]
    public async Task<IActionResult> Review(Guid id, CancellationToken ct, bool useCandidate = false)
    {
        var item = await queries.GetAsync(user.TenantId, id, ct);
        if (item is null) return NotFound();
        var source = useCandidate && item.PendingCandidateJson is not null ? JsonSerializer.Deserialize<BillingExtractionDto>(item.PendingCandidateJson)! : item;
        var input = ToReviewInput(source);
        input.Id = item.Id; input.DocumentTitle = item.DocumentTitle; input.Revision = item.Revision;
        input.UseCandidate = useCandidate && item.PendingCandidateJson is not null;
        input.ResponsibleUserId = item.ResponsibleUserId; input.ReviewDueDate = item.ReviewDueDate; input.Priority = item.Priority;
        await PrepareReviewAsync(item, input, ct);
        return View(input);
    }

    [HttpPost("Review/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(Guid id, BillingReviewInput input, [FromServices] IBillingCommands commands, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var existing = await queries.GetAsync(user.TenantId, id, ct);
            if (existing is null) return NotFound();
            input.Id = id; input.DocumentTitle = existing.DocumentTitle;
            await PrepareReviewAsync(existing, input, ct);
            return View(input);
        }
        var ok = await commands.ReviewAsync(user.TenantId, id, user.UserId, input, ct);
        if (!ok)
        {
            var existing = await queries.GetAsync(user.TenantId, id, ct);
            if (existing is null) return NotFound();
            ModelState.AddModelError("", "A revisão não foi salva. Confira o checklist, as regras obrigatórias, duplicidades e o total parcelado. Se o registro mudou, recarregue a revisão para comparar os dados atuais.");
            input.Id = id; input.DocumentTitle = existing.DocumentTitle;
            await PrepareReviewAsync(existing, input, ct);
            return View(input);
        }
        TempData["Ok"] = "Revisão registrada com usuário, data e justificativa.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("Rules")]
    public async Task<IActionResult> Rules(Guid? edit, CancellationToken ct)
    {
        ViewBag.DuplicatePolicy = await operations.DuplicatePolicyAsync(user.TenantId, ct);
        var items = await rules.ListAsync(user.TenantId, ct);
        var form = edit.HasValue ? items.FirstOrDefault(x => x.Id == edit.Value) ?? new BillingExtractionRuleInput() : new BillingExtractionRuleInput();
        return View(new BillingRulesPageVm(items, form));
    }

    [HttpPost("Rules")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRule(BillingExtractionRuleInput input, CancellationToken ct)
    {
        ViewBag.DuplicatePolicy = await operations.DuplicatePolicyAsync(user.TenantId, ct);
        if (!ModelState.IsValid)
            return View("Rules", new BillingRulesPageVm(await rules.ListAsync(user.TenantId, ct), input));
        await rules.SaveAsync(user.TenantId, user.UserId, input, ct);
        TempData["Ok"] = "Regra de extração salva e disponível para os próximos OCRs.";
        return RedirectToAction(nameof(Rules));
    }

    [HttpPost("DuplicatePolicy"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDuplicatePolicy(BillingDuplicatePolicy input, CancellationToken ct)
    {
        await operations.SaveDuplicatePolicyAsync(user.TenantId, user.UserId, input, ct);
        TempData["Ok"] = "Critérios de duplicidade atualizados.";
        return RedirectToAction(nameof(Rules));
    }

    [HttpPost("Rules/{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        TempData[await rules.DeleteAsync(user.TenantId, user.UserId, id, ct) ? "Ok" : "Err"] = "Regra removida.";
        return RedirectToAction(nameof(Rules));
    }

    private async Task PrepareReviewAsync(BillingExtractionDto item, BillingReviewInput input, CancellationToken ct)
    {
        var source = input.UseCandidate && item.PendingCandidateJson is not null ? JsonSerializer.Deserialize<BillingExtractionDto>(item.PendingCandidateJson)! : item;
        ViewData["Checklist"] = BillingChecklist.EvaluateReview(source, input);
        ViewBag.Stored = item; ViewBag.Source = source;
        ViewBag.Assignees = await operations.AssigneesAsync(user.TenantId, ct);
    }

    [HttpPost("Installments"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddInstallment(BillingInstallmentInput input, CancellationToken ct)
    {
        var ok = ModelState.IsValid && await operations.AddInstallmentAsync(user.TenantId, user.UserId, input, ct);
        TempData[ok ? "Ok" : "Err"] = ok ? "Parcela registrada." : "Confira a aprovação, a sequência, o sentido da operação e o total disponível para parcelar.";
        return RedirectToAction(nameof(Details), new { id = input.ExtractionId });
    }

    [HttpPost("Settlements"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Settle(BillingSettlementInput input, CancellationToken ct)
    {
        var ok = ModelState.IsValid && await operations.SettleAsync(user.TenantId, user.UserId, input, ct);
        TempData[ok ? "Ok" : "Err"] = ok ? "Liquidação registrada." : "Liquidação não aplicada. Confira a aprovação e o saldo da parcela.";
        return RedirectToAction(nameof(Details), new { id = input.ExtractionId });
    }

    [HttpPost("Reconcile"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reconcile(Guid id, Guid extractionId, CancellationToken ct)
    {
        var data = await operations.GetAsync(user.TenantId, extractionId, ct);
        var ok = data.Settlements.Any(x => x.Id == id) && await operations.ReconcileAsync(user.TenantId, user.UserId, id, ct);
        TempData[ok ? "Ok" : "Err"] = ok ? "Conciliação registrada." : "Registro não encontrado ou já conciliado.";
        return RedirectToAction(nameof(Details), new { id = extractionId });
    }

    [HttpGet("Export")]
    public async Task<IActionResult> Export([FromQuery] BillingFilter filter, CancellationToken ct)
    {
        var csv = new StringBuilder("Documento;Fornecedor;Nota;Competência;Bruto;Status\r\n");
        var rows = await queries.ExportAsync(user.TenantId, filter, ct);
        foreach (var x in rows) csv.AppendJoin(';', Csv(x.DocumentTitle), Csv(x.SupplierName), Csv(x.InvoiceNumber), Csv(x.CompetenceMonth), Csv(x.GrossAmount?.ToString("0.00", CultureInfo.InvariantCulture)), Csv(x.ExtractionStatus)).Append("\r\n");
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", "faturamento.csv");
    }

    private static string Csv(string? value)
    {
        var safe = value ?? "";
        if (safe.TrimStart().StartsWith('=') || safe.TrimStart().StartsWith('+') || safe.TrimStart().StartsWith('-') || safe.TrimStart().StartsWith('@')) safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private static BillingReviewInput ToReviewInput(BillingExtractionDto item) => new()
    {
        Revision = item.Revision, ResponsibleUserId = item.ResponsibleUserId, ReviewDueDate = item.ReviewDueDate,
        Priority = item.Priority, DivergenceReason = item.DivergenceReason, HasDivergence = item.ExtractionStatus == "DIVERGENT",
        Id = item.Id, DocumentTitle = item.DocumentTitle, SupplierName = item.SupplierName, SupplierDocument = item.SupplierDocument,
        InvoiceNumber = item.InvoiceNumber, InvoiceSeries = item.InvoiceSeries, IssueDate = item.IssueDate, DueDate = item.DueDate,
        CompetenceMonth = item.CompetenceMonth, GrossAmount = item.GrossAmount, NetAmount = item.NetAmount, TaxAmount = item.TaxAmount,
        ContractNumber = item.ContractNumber, PurchaseOrder = item.PurchaseOrder, CostCenter = item.CostCenter,
        ServiceDescription = item.ServiceDescription, UstQuantity = item.UstQuantity, UstUnitValue = item.UstUnitValue
    };
}
