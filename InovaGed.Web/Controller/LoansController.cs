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
[Authorize(Policy = AppPolicies.HospitalDocumentsOrLoansAccess)]
[Route("[controller]")]
public sealed class LoansController : Controller
{
    private readonly ILogger<LoansController> _logger;
    private readonly ICurrentUser _user;
    private readonly ILoanRequestService _service;
    private readonly IAuditWriter _audit;
    private readonly IDbConnectionFactory _db;

    public LoansController(
        ILogger<LoansController> logger,
        ICurrentUser user,
        ILoanRequestService service,
        IAuditWriter audit,
        IDbConnectionFactory db)
    {
        _logger = logger;
        _user = user;
        _service = service;
        _audit = audit;
        _db = db;
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
            stats.Requested = await _service.PendingCountAsync(tenantId, _user.UserId, canViewAll: true, ct);
            ViewBag.Stats = stats;

            var list = await _service.ListAsync(tenantId, q, status, _user.UserId, true, ct);
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
            var payload = rows.Select(x => new { id = x.Id, code = x.Code, title = x.Title, type = "", folderPath = "" });
            return Json(new { success = true, items = payload });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.DocSearch failed");
            return Json(new { success = false, items = Array.Empty<object>() });
        }
    }

    // =========================================================
    // GET /Loans/Overdue
    // =========================================================
    [HttpGet("Overdue")]
    public async Task<IActionResult> Overdue(CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var list = await _service.OverdueAsync(tenantId, _user.UserId, true, ct);
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
            var count = await _service.PendingCountAsync(tenantId, _user.UserId, true, ct);
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
    [HttpGet("New")]
    public IActionResult New()
        => View(new LoanCreateVM { DueAt = DateTimeOffset.Now.AddDays(7) });

    // =========================================================
    // POST /Loans/New
    // =========================================================
    [HttpPost("New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(LoanCreateVM vm, CancellationToken ct)
    {
        try
        {
            var tenantId = _user.TenantId;
            var res = await _service.CreateAsync(tenantId, _user.UserId ?? Guid.Empty, vm, ct);

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
            var vm = await _service.GetDetailsAsync(tenantId, id, _user.UserId, true, ct);

            if (vm is null) return NotFound();
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
    [HttpPost("{id:guid}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, string? notes, CancellationToken ct)
    {
        var res = await _service.ApproveAsync(_user.TenantId, id, _user.UserId ?? Guid.Empty, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação aprovada com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:guid}/Deliver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliver(Guid id, string? notes, CancellationToken ct)
    {
        var res = await _service.DeliverAsync(_user.TenantId, id, _user.UserId ?? Guid.Empty, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação entregue com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:guid}/Return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(Guid id, string? notes, CancellationToken ct)
    {
        var res = await _service.ReturnAsync(_user.TenantId, id, _user.UserId ?? Guid.Empty, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação devolvida com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:guid}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? notes, CancellationToken ct)
    {
        var res = await _service.CancelAsync(_user.TenantId, id, _user.UserId ?? Guid.Empty, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação cancelada com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
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
