using InovaGed.Application.HospitalBilling;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.HospitalDocumentsAccess)]
[Route("HospitalBilling")]
public sealed class HospitalBillingController(ICurrentUser user, IHospitalBillingQueries queries) : Controller
{
    [HttpGet("")][HttpGet("Dashboard")]
    public async Task<IActionResult> Index(string? insurer, string? competence, string? status, bool? hasDenial, CancellationToken ct)
    { var filter = new HospitalBillingFilter(insurer, competence, status, hasDenial); ViewBag.Filter = filter; return View(await queries.DashboardAsync(user.TenantId, filter, ct)); }
    [HttpGet("Documents")] public Task<IActionResult> Documents(string? insurer, string? competence, string? status, bool? hasDenial, CancellationToken ct) => Index(insurer, competence, status, hasDenial, ct);
    [HttpGet("Review")][HttpGet("Glosas")] public Task<IActionResult> WorkQueue(CancellationToken ct) => Index(null, null, Request.Path.Value!.EndsWith("Glosas", StringComparison.OrdinalIgnoreCase) ? null : "PENDING_REVIEW", Request.Path.Value!.EndsWith("Glosas", StringComparison.OrdinalIgnoreCase) ? true : null, ct);
    [HttpGet("Rules")]
    public IActionResult Rules() => View(new HospitalBillingRulesCatalog(
    [
        new("Guia TISS / SADT", "documents", ["guia", "TISS", "SADT", "TUSS"], ["Convênio", "Prestador", "Número da guia", "Autorização"], "Conferir beneficiário mascarado, autorização, procedimento e valores."),
        new("AIH", "report", ["AIH", "SUS", "internação"], ["Número da AIH", "CNES", "Competência", "Procedimento"], "Validar competência, caráter da internação e código SIGTAP."),
        new("APAC / BPA", "classification", ["APAC", "BPA", "SIGTAP"], ["Autorização", "CNES", "Competência", "Procedimento"], "Comparar produção apresentada com autorização e competência."),
        new("Fatura hospitalar", "report", ["fatura", "conta hospitalar", "valor apresentado"], ["Convênio", "Fatura", "Competência", "Valor apresentado"], "Reconciliar guias do lote e totais financeiros."),
        new("Demonstrativo de glosa", "warning", ["glosa", "motivo", "valor glosado"], ["Fatura", "Motivo da glosa", "Valor glosado"], "Classificar motivo, prazo e elegibilidade para recurso."),
        new("Recurso / protocolo", "protocol", ["recurso de glosa", "protocolo de envio"], ["Protocolo", "Lote", "Valor recuperado"], "Rastrear envio, prazo, resposta e recuperação financeira.")
    ],
    ["Valor aprovado + glosado não pode superar o apresentado.", "Guia, autorização e competência devem corresponder ao documento-fonte.", "Confiança inferior a 70% exige revisão humana.", "Paciente permanece mascarado em filas e listagens."]));
    [HttpGet("Details/{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) => await queries.GetAsync(user.TenantId, id, ct) is { } item ? View(item) : NotFound();
}
