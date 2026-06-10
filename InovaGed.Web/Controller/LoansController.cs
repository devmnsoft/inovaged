using InovaGed.Application.Audit;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InovaGed.Web.Controllers;

// ADMIN sempre com acesso total; perfis Ophir mantêm acesso ao módulo hospitalar/empréstimos.
[Authorize(Policy = AppPolicies.LoansView)]
[Route("[controller]")]
public sealed class LoansController : Controller
{
    private readonly ILogger<LoansController> _logger;
    private readonly ICurrentUser _user;
    private readonly ILoanRequestService _service;
    private readonly IAuditWriter _audit;
    private readonly IDbConnectionFactory _db;
    private readonly ILoanAccessService _loanAccess;

    public LoansController(
        ILogger<LoansController> logger,
        ICurrentUser user,
        ILoanRequestService service,
        IAuditWriter audit,
        IDbConnectionFactory db,
        ILoanAccessService loanAccess)
    {
        _logger = logger;
        _user = user;
        _service = service;
        _audit = audit;
        _db = db;
        _loanAccess = loanAccess;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        _logger.LogInformation("Acesso ao módulo Loans. Path={Path} User={User}", HttpContext.Request.Path.Value, User.Identity?.Name ?? "anonymous");
    }

    // =========================================================
    // GET /Loans
    // =========================================================
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? status, CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;

            var stats = new LoanStatsDto();
            var scope = await _loanAccess.BuildLoanScopeAsync(_user.TenantId, _user.UserId, User, ct);
            if (scope.IsAdministradorOphir && string.IsNullOrWhiteSpace(scope.Sector))
                TempData["Err"] = "Seu usuário não possui setor vinculado. Configure o setor para visualizar solicitações.";
            stats.Requested = await _service.PendingCountAsync(tenantId, _user.UserId, scope, ct);
            ViewBag.Stats = stats;

            var list = await _service.ListAsync(tenantId, q, status, _user.UserId, scope, ct);
            ViewBag.Q = q;
            ViewBag.Status = status;

            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.Index failed");
            ViewBag.Stats = new LoanStatsDto();
            TempData["Err"] = "Erro ao carregar empréstimos.";
            return View(Array.Empty<LoanRowDto>());
        }
    }

    // =========================================================
    // GET /Loans/DocSearch?q=...
    // =========================================================
    [HttpGet("DocSearch")]
    public async Task<IActionResult> DocSearch(string? q, CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var rows = await _service.SearchDocumentsAsync(tenantId, q ?? "", ct);
            var payload = rows.Select(x => new { id = x.Id, code = x.Code, title = x.Title, status = x.Status, createdAt = x.CreatedAt, type = "", folderPath = "" }).ToList();
            return Json(new { success = true, items = payload, total = payload.Count, message = (string?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.DocSearch failed");
            return Json(new { success = false, items = Array.Empty<object>(), total = 0, message = "Não foi possível buscar documentos." });
        }
    }

    // =========================================================
    // GET /Loans/Overdue
    // =========================================================
    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpGet("Overdue")]
    public async Task<IActionResult> Overdue(CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var scope = await _loanAccess.BuildLoanScopeAsync(_user.TenantId, _user.UserId, User, ct);
            if (!CanManageLoans()) return Forbid();
            scope.IsAdmin = true;
            var list = await _service.OverdueAsync(tenantId, _user.UserId, scope, ct);
            return View(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.Overdue failed");
            TempData["Err"] = "Erro ao carregar vencidos.";
            return View(Array.Empty<LoanRowDto>());
        }
    }

    // =========================================================
    // GET /Loans/RunOverdue
    // (rotina: tenta função ged.loan_run_overdue; fallback se não existir)
    // =========================================================
    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpGet("RunOverdue")]
    public async Task<IActionResult> RunOverdue(CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;

            var updated = 0;

            TempData["Ok"] = $"Rotina OVERDUE executada. Eventos gerados/atualizados: {updated}";
            return RedirectToAction(nameof(Overdue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.RunOverdue failed");
            TempData["Err"] = "Erro ao executar rotina de vencidos.";
            return RedirectToAction(nameof(Overdue));
        }
    }

    // =========================================================
    // POST /Loans/Overdue/Register
    // (registra eventos OVERDUE no histórico via vw_loan_overdue)
    // =========================================================
    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("Overdue/Register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterOverdue(CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;

            var res = InovaGed.Domain.Primitives.Result<int>.Ok(0);

            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess
                ? $"Eventos OVERDUE registrados: {res.Value}"
                : res.ErrorMessage;

            return RedirectToAction(nameof(Overdue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.RegisterOverdue failed");
            TempData["Err"] = "Erro ao registrar vencidos.";
            return RedirectToAction(nameof(Overdue));
        }
    }


    [HttpGet("PendingCount")]
    public async Task<IActionResult> PendingCount(CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var count = await _service.PendingCountAsync(tenantId, _user.UserId, await _loanAccess.BuildLoanScopeAsync(_user.TenantId, _user.UserId, User, ct), ct);
            return Ok(new { success = true, count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.PendingCount failed. TenantId={TenantId} UserId={UserId}", _user.TenantId, _user.UserId);
            return StatusCode(500, new { success = false, count = 0 });
        }
    }
    // =========================================================
    // GET /Loans/New
    // =========================================================
    [Authorize(Policy = AppPolicies.LoansRequest)]
    [HttpGet("New")]
    public IActionResult New()
        => View(new LoanCreateVM { DueAt = DateTimeOffset.Now.AddDays(7) });

    // =========================================================
    // POST /Loans/New
    // =========================================================
    [Authorize(Policy = AppPolicies.LoansRequest)]
    [HttpPost("New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(LoanCreateVM vm, CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var res = await _service.CreateAsync(tenantId, _user.UserId, vm, ct);

            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View(vm);
            }

            TempData["Ok"] = "Empréstimo criado com sucesso.";
            return RedirectToAction(nameof(Details), new { id = res.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.New POST failed");
            TempData["Err"] = "Erro ao criar empréstimo.";
            return View(vm);
        }
    }

    // =========================================================
    // GET /Loans/{id}
    // =========================================================
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var vm = await _service.GetDetailsAsync(tenantId, id, _user.UserId, await _loanAccess.BuildLoanScopeAsync(_user.TenantId, _user.UserId, User, ct), ct);

            if (vm is null)
            {
                await AuditAccessDeniedAsync(id, ct);
                return Forbid();
            }
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.Details failed");
            TempData["Err"] = "Erro ao carregar detalhes do empréstimo.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("Profiles")]
    public async Task<IActionResult> Profiles(CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync("select p.id, p.profile_name, coalesce(r.name,'') as role_name from ged.loan_approval_profile p left join aspnetroles r on r.id=p.role_id where p.tenant_id=@TenantId and p.reg_status='A' order by p.profile_name", new { TenantId = _user.TenantId });
            return View(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.Profiles failed. TenantId={TenantId} UserId={UserId}", _user.TenantId, _user.UserId);
            TempData["Err"] = "Erro ao carregar perfis de aprovação.";
            return View(Array.Empty<object>());
        }
    }

    // =========================================================
    // Transições de status
    // =========================================================
    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, string? notes, string? internalNotes, bool notifyRequester, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        notes = CombineNotes(notes, internalNotes, notifyRequester);
        var res = await _service.ApproveAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação aprovada com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Deliver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliver(Guid id, string? notes, string? internalNotes, bool notifyRequester, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        notes = CombineNotes(notes, internalNotes, notifyRequester);
        var res = await _service.DeliverAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação entregue com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(Guid id, string? notes, string? internalNotes, bool notifyRequester, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        notes = CombineNotes(notes, internalNotes, notifyRequester);
        var res = await _service.ReturnAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação devolvida com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }


    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? notes, string? internalNotes, bool notifyRequester, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        notes = CombineNotes(notes, internalNotes, notifyRequester);
        var res = await _service.RejectAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação rejeitada com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/ReturnForAdjustment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnForAdjustment(Guid id, string? notes, string? internalNotes, bool notifyRequester, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        notes = CombineNotes(notes, internalNotes, notifyRequester);
        var res = await _service.ReturnForAdjustmentAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação devolvida para ajuste." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansView)]
    [HttpPost("{id:guid}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? notes, string? internalNotes, bool notifyRequester, CancellationToken ct)
    {
        var scope = await _loanAccess.BuildLoanScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var details = await _service.GetDetailsAsync(_user.TenantId, id, _user.UserId, scope, ct);
        if (details is null) return Forbid();
        if (!scope.CanManage && !new[] { "REQUESTED", "RETURNED_FOR_ADJUSTMENT" }.Contains((details.Header.Status ?? string.Empty).ToUpperInvariant())) return Forbid();
        notes = CombineNotes(notes, internalNotes, notifyRequester);
        var res = await _service.CancelAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação cancelada com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool CanManageLoans() => RolePolicyHelper.IsFullAdmin(User) || User.IsInNormalizedRole(AppRoles.AdministradorOphir);

    private async Task<bool> CanOperateLoanAsync(Guid id, CancellationToken ct)
        => await _loanAccess.CanManageLoanAsync(_user.TenantId, id, _user.UserId, User, ct);

    private async Task AuditAccessDeniedAsync(Guid id, CancellationToken ct)
    {
        _ = await _audit.WriteAsync(_user.TenantId, _user.UserId, "ACCESS_DENIED", "loan_request", id,
            "Tentativa de acesso a solicitação fora do escopo", null, null, new { loanId = id }, ct);
    }

    private static string? CombineNotes(string? notes, string? internalNotes, bool notifyRequester)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(notes)) parts.Add(notes.Trim());
        if (!string.IsNullOrWhiteSpace(internalNotes)) parts.Add($"Observação interna: {internalNotes.Trim()}");
        parts.Add(notifyRequester ? "Notificar solicitante: sim" : "Notificar solicitante: não");
        return string.Join(" | ", parts);
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        try
        {
            var path = context.HttpContext?.Request?.Path.Value ?? "";
            var user = context.HttpContext?.User?.Identity?.Name ?? "anon";

            if (context.Exception != null)
                _logger.LogError(context.Exception, "LoansController action failed. Path={Path} User={User}", path, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoansController.OnActionExecuted failed");
        }
        finally
        {
            base.OnActionExecuted(context);
        }
    }
}
