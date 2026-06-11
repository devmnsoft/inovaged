using System.Net;
using System.Text;
using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Application.Operations;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.OperationsAccess)]
[Route("[controller]")]
public sealed class OperationsController : Controller
{
    private readonly IOperationsDashboardService _service;
    private readonly ITableSchemaGuard _schema;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly ILogger<OperationsController> _logger;

    public OperationsController(IOperationsDashboardService service, ITableSchemaGuard schema, ICurrentUser currentUser, IAuditWriter audit, ILogger<OperationsController> logger)
    {
        _service = service;
        _schema = schema;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return RedirectToAction("AccessDenied", "Account");
        await AuditAsync("OPERATIONS_VIEW", "Visualização da Central Operacional", "summary", filter, ct);
        var vm = await _service.GetSummaryAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct);
        return View(vm);
    }

    [HttpGet("Summary")]
    public async Task<IActionResult> Summary([FromQuery] OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_FILTER_APPLIED", "Resumo da Central Operacional atualizado", "summary", filter, ct);
        return Json(await _service.GetSummaryAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct));
    }

    [HttpGet("Queue")]
    public Task<IActionResult> Queue([FromQuery] string type, [FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync(type, filter, ct);

    [HttpGet("GedQueue")]
    public Task<IActionResult> GedQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("ged", filter, ct);

    [HttpGet("OcrQueue")]
    public Task<IActionResult> OcrQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("ocr", filter, ct);

    [HttpGet("LoansQueue")]
    public Task<IActionResult> LoansQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("loans", filter, ct);

    [HttpGet("LoanQueue")]
    public Task<IActionResult> LoanQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("loans", filter, ct);

    [HttpGet("ProtocolsQueue")]
    public Task<IActionResult> ProtocolsQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("protocol", filter, ct);

    [HttpGet("ProtocolQueue")]
    public Task<IActionResult> ProtocolQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("protocol", filter, ct);

    [HttpGet("QualityQueue")]
    public Task<IActionResult> QualityQueue([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("quality", filter, ct);

    [HttpGet("Alerts")]
    public Task<IActionResult> Alerts([FromQuery] OperationsDashboardFilter filter, CancellationToken ct) => QueueResponseAsync("alerts", filter, ct);


    [HttpPost("ActionClicked")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActionClicked([FromForm] string module, [FromForm] string actionUrl, [FromForm] string actionLabel, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        await AuditAsync("OPERATIONS_ACTION_CLICKED", "Clique em ação da Central Operacional", module, new { actionUrl, actionLabel }, ct);
        return Json(new { success = true });
    }

    [HttpPost("Revalidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revalidate(CancellationToken ct)
    {
        if (!CanAccessOperations() || !IsTechnicalAdmin()) return Forbid();
        await AuditAsync("OPERATIONS_FILTER_APPLIED", "Revalidação manual de schema da Central", "schema", new { path = Request.Path }, ct);
        var modules = new[] { "GED", "OCR", "Documentos Parciais", "Loans", "Protocolo", "Qualidade", "Alertas" };
        var statuses = new List<ModuleSchemaStatus>();
        foreach (var module in modules) statuses.Add(await _schema.GetModuleStatusAsync(module, ct));
        return Json(new { success = true, moduleReady = statuses.All(x => x.IsReady), total = statuses.Count(x => !x.IsReady), html = RenderSchemaStatus(statuses), message = statuses.Any(x => !x.IsReady) ? "Alguns módulos ainda não estão configurados." : null });
    }

    private async Task<IActionResult> QueueResponseAsync(string type, OperationsDashboardFilter filter, CancellationToken ct)
    {
        if (!CanAccessOperations()) return Forbid();
        var normalized = NormalizeQueueType(type);
        try
        {
            await AuditAsync("OPERATIONS_TAB_VIEW", "Abertura de aba da Central Operacional", normalized, filter, ct);
            var page = normalized switch
            {
                "ged" => await _service.GetGedQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                "ocr" => await _service.GetOcrQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                "partials" => await _service.GetPartialDocumentsQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                "loans" => await _service.GetLoanQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                "protocol" => await _service.GetProtocolQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                "quality" => await _service.GetQualityQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                "alerts" => await _service.GetAlertsAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct),
                _ => await _service.GetGedQueueAsync(_currentUser.TenantId, _currentUser.UserId, _currentUser.Roles, filter, ct)
            };

            if (!page.ModuleReady) await AuditAsync("OPERATIONS_MODULE_NOT_READY", page.Message ?? "Módulo não configurado", normalized, filter, ct);

            return Json(new
            {
                success = true,
                moduleReady = page.ModuleReady,
                total = page.Total,
                html = RenderQueue(normalized, page),
                message = page.Message,
                items = page.Items,
                page.Page,
                page.PageSize,
                page.EmptyMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OPERATIONS_QUERY_FAILED: falha inesperada na fila {Queue}.", normalized);
            await AuditAsync("OPERATIONS_QUERY_FAILED", "Falha inesperada ao carregar fila operacional", normalized, new { filter, error = ex.Message }, ct);
            return Json(new { success = false, message = "Não foi possível carregar esta fila." });
        }
    }

    private string RenderQueue(string type, OperationQueuePageDto page)
    {
        if (!page.ModuleReady)
        {
            var tech = IsTechnicalAdmin() ? "<div class=\"mt-3 d-flex gap-2 justify-content-center\"><a class=\"btn btn-sm btn-primary\" href=\"/SchemaHealth\">Abrir Schema Health</a><a class=\"btn btn-sm btn-outline-secondary\" href=\"/SystemHealth\">Ver Startup Guard</a></div>" : string.Empty;
            return $"<div class=\"ops-empty\"><i class=\"bi bi-tools fs-2 d-block mb-2\"></i><strong>{E(page.Message ?? page.EmptyMessage)}</strong>{tech}</div>";
        }
        if (page.Items.Count == 0) return $"<div class=\"ops-empty\"><i class=\"bi bi-check2-circle fs-2 d-block mb-2 text-success\"></i>{E(page.EmptyMessage)}</div>";

        var headers = type switch
        {
            "ocr" => new[] { "Documento", "Arquivo", "Pasta", "Status OCR", "Última tentativa", "Erro", "Ação" },
            "partials" => new[] { "Documento", "Partes recebidas", "OCR das partes", "Última parte", "Status", "Ação" },
            "loans" => new[] { "Protocolo", "Solicitante", "Setor", "Status", "Vencimento", "Itens", "Última movimentação", "Ação" },
            "protocol" => new[] { "Protocolo", "Título", "Solicitante", "Setor origem", "Setor destino", "Status", "Prioridade", "Prazo", "Responsável", "Ação" },
            "quality" => new[] { "Documento", "Score", "Status", "Pendências", "Próxima ação", "Última análise", "Ação" },
            "alerts" => new[] { "Alerta", "Módulo", "Severidade", "Referência", "Data", "Ação" },
            _ => new[] { "Documento", "Pasta", "Tipo", "Classificação", "Upload em", "Responsável", "Ação" }
        };
        var sb = new StringBuilder("<div class=\"table-responsive\"><table class=\"table table-sm align-middle ops-table\"><thead><tr>");
        foreach (var h in headers) sb.Append("<th>").Append(E(h)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var item in page.Items) sb.Append(RenderRow(type, item));
        sb.Append("</tbody></table></div><div class=\"text-muted small mt-2\">Total: ").Append(page.Total).Append("</div>");
        return sb.ToString();
    }

    private static string RenderRow(string type, OperationQueueItemDto x) => type switch
    {
        "ocr" => $"<tr><td>{Doc(x)}</td><td>{E(x.Code)}</td><td>{E(x.Folder)}</td><td>{Badge(x.Status ?? x.Ocr, x.Severity)}</td><td>{Dt(x.LastAttemptAt)}</td><td class=\"text-danger small\">{E(x.Error)}</td><td>{Action(x)}</td></tr>",
        "partials" => $"<tr><td>{Doc(x)}</td><td>{E($"{x.Parts ?? 0}/{x.ExpectedParts ?? 2}")}</td><td>{E(x.Ocr)}</td><td>{Dt(x.UpdatedAt)}</td><td>{Badge(x.Status, x.Severity)}</td><td>{Action(x)}</td></tr>",
        "loans" => $"<tr><td>{E(x.Protocol)}</td><td>{E(x.Requester)}</td><td>{E(x.Sector)}</td><td>{Badge(x.Status, x.Severity)}</td><td>{Dt(x.DueAt)}</td><td>{E(x.ItemsCount)}</td><td>{Dt(x.UpdatedAt)}</td><td>{Action(x)}</td></tr>",
        "protocol" => $"<tr><td>{E(x.Protocol)}</td><td>{E(x.Title)}</td><td>{E(x.Requester)}</td><td>{E(x.Sector)}</td><td>{E(x.DestinationSector)}</td><td>{Badge(x.Status, x.Severity)}</td><td>{E(x.Priority)}</td><td>{Dt(x.DueAt)}</td><td>{E(x.Responsible)}</td><td>{Action(x)}</td></tr>",
        "quality" => $"<tr><td>{Doc(x)}</td><td>{E(x.Score)}</td><td>{Badge(x.Status, x.Severity)}</td><td>{E(x.PendingIssues)}</td><td>{E(x.NextStep)}</td><td>{Dt(x.LastAnalyzedAt)}</td><td>{Action(x)}</td></tr>",
        "alerts" => $"<tr><td>{E(x.Title)}</td><td>{E(x.Status ?? x.Queue)}</td><td>{Badge(x.Severity, x.Severity)}</td><td>{E(x.Code ?? x.Protocol ?? x.Error)}</td><td>{Dt(x.UpdatedAt ?? x.UploadedAt ?? x.DueAt)}</td><td>{Action(x)}</td></tr>",
        _ => $"<tr><td>{Doc(x)}</td><td>{E(x.Folder)}</td><td>{E(x.DocumentType)}</td><td>{E(x.Classification)}</td><td>{Dt(x.UploadedAt)}</td><td>{E(x.Responsible ?? x.Requester)}</td><td>{Action(x)}</td></tr>"
    };

    private string RenderSchemaStatus(IEnumerable<ModuleSchemaStatus> statuses)
        => string.Join(string.Empty, statuses.Select(x => $"<div class=\"d-flex justify-content-between border-bottom py-2\"><span>{E(x.ModuleName)}</span><span class=\"badge bg-{(x.IsReady ? "success" : "warning text-dark")}\">{E(x.StatusText)}</span></div>"));

    private bool CanAccessOperations()
        => RolePolicyHelper.IsFullAdmin(User) || User.IsInNormalizedRole(AppRoles.AdministradorOphir) || User.IsInNormalizedRole(AppRoles.ArquivistaOphir);

    private bool IsTechnicalAdmin() => RolePolicyHelper.IsFullAdmin(User) || User.IsInNormalizedRole(AppRoles.Administrador);
    private static string NormalizeQueueType(string? type) => (type ?? "ged").Trim().ToLowerInvariant() switch { "partial" or "partials" or "documentos-parciais" => "partials", "loan" or "loans" => "loans", "protocols" or "protocolo" or "protocol" => "protocol", "qualidade" or "quality" => "quality", "alert" or "alerts" or "alertas" => "alerts", "ocr" => "ocr", _ => "ged" };
    private static string Doc(OperationQueueItemDto x) => $"<strong>{E(x.Title)}</strong><div class=\"small text-muted\">{E(x.Code)}</div>";
    private static string Action(OperationQueueItemDto x) => $"<a class=\"btn btn-sm btn-outline-primary ops-action\" data-ops-action href=\"{E(x.ActionUrl)}\">{E(x.ActionLabel)}</a>";
    private static string Badge(string? text, string? severity) => $"<span class=\"badge bg-{Css(severity)}\">{E(text)}</span>";
    private static string Css(string? severity) => severity is "critical" ? "danger" : severity is "high" ? "warning text-dark" : severity is "medium" ? "info text-dark" : "secondary";
    private static string Dt(DateTime? value) => value.HasValue ? WebUtility.HtmlEncode(value.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")) : "-";
    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "-");

    private Task AuditAsync(string action, string summary, string module, object data, CancellationToken ct)
        => _audit.WriteAsync(_currentUser.TenantId, _currentUser.UserId, action, "OPERATIONS", null, summary, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { module, filter = data, path = Request.Path.ToString(), correlationId = HttpContext.TraceIdentifier, timestamp = DateTimeOffset.UtcNow }, ct);
}
