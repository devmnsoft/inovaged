using InovaGed.Application.Audit;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class LoansController : Controller
{
    private readonly ILogger<LoansController> _logger;
    private readonly ICurrentUser _user;
    private readonly ILoanQueries _queries;
    private readonly ILoanCommands _commands;
    private readonly IAuditWriter _audit;

    public LoansController(
        ILogger<LoansController> logger,
        ICurrentUser user,
        ILoanQueries queries,
        ILoanCommands commands,
        IAuditWriter audit)
    {
        _logger = logger;
        _user = user;
        _queries = queries;
        _commands = commands;
        _audit = audit;
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

            var stats = await _queries.StatsAsync(tenantId, ct);
            ViewBag.Stats = stats;

            var list = await _queries.ListAsync(tenantId, q, status, ct);
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
            var rows = await _queries.SearchDocumentsAsync(tenantId, q ?? "", ct);

            // camelCase pro JS da View
            var payload = rows.Select(x => new { id = x.Id, code = x.Code, title = x.Title });
            return Json(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.DocSearch failed");
            return Json(Array.Empty<object>());
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
            var list = await _queries.ListOverdueAsync(tenantId, ct);
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

            var updated = await _commands.RunOverdueAsync(tenantId, ct);

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

            var res = await _commands.RegisterOverdueEventsAsync(tenantId, _user.UserId, ct);

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
            var res = await _commands.CreateAsync(tenantId, _user.UserId, vm, ct);

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
            var vm = await _queries.GetAsync(tenantId, id, ct);

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

    // =========================================================
    // Transições de status
    // =========================================================
    [HttpPost("{id:guid}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, string? notes, CancellationToken ct)
        => await Transition(id, "Approve", (t, l, u) => _commands.ApproveAsync(t, l, u, notes, ct));

    [HttpPost("{id:guid}/Deliver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliver(Guid id, string? notes, CancellationToken ct)
        => await Transition(id, "Deliver", (t, l, u) => _commands.DeliverAsync(t, l, u, notes, ct));

    [HttpPost("{id:guid}/Return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(Guid id, string? notes, CancellationToken ct)
        => await Transition(id, "Return", (t, l, u) => _commands.ReturnAsync(t, l, u, notes, ct));

    private async Task<IActionResult> Transition(
        Guid id,
        string action,
        Func<Guid, Guid, Guid?, Task<InovaGed.Domain.Primitives.Result>> fn)
    {
        try
        {
            var tenantId = _user.TenantId;
            var res = await fn(tenantId, id, _user.UserId);

            TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Ok." : res.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loans.{Action} failed. Loan={Loan}", action, id);
            TempData["Err"] = "Erro ao atualizar empréstimo.";
            return RedirectToAction(nameof(Details), new { id });
        }
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