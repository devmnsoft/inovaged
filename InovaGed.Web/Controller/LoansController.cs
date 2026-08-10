using InovaGed.Application.Audit;
using Dapper;
using System.Security.Cryptography;
using System.Text;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Identity;
using InovaGed.Application.SmartSearch;
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
    private readonly ISmartSearchService _smartSearch;
    private readonly ISecureDocumentLinkService _secureLinks;
    private readonly ILoanOverdueService _overdue;
    private readonly ILoanCollectionService _collections;
    private readonly ILoanReportService _reports;

    public LoansController(
        ILogger<LoansController> logger,
        ICurrentUser user,
        ILoanRequestService service,
        IAuditWriter audit,
        IDbConnectionFactory db,
        ILoanAccessService loanAccess,
        ISmartSearchService smartSearch,
        ISecureDocumentLinkService secureLinks,
        ILoanOverdueService overdue,
        ILoanCollectionService collections,
        ILoanReportService reports)
    {
        _logger = logger;
        _user = user;
        _service = service;
        _audit = audit;
        _db = db;
        _loanAccess = loanAccess;
        _smartSearch = smartSearch;
        _secureLinks = secureLinks;
        _overdue = overdue;
        _collections = collections;
        _reports = reports;
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
            var result = await _smartSearch.SearchAsync(new SmartSearchRequest
            {
                TenantId = tenantId,
                UserId = _user.UserId,
                Query = q ?? string.Empty,
                PageSize = 15,
                Source = "LOANS_DOC_SEARCH",
                IsAdmin = CanManageLoans()
            }, ct);
            _ = await _audit.WriteAsync(tenantId, _user.UserId, "LOAN_REQUEST_SMART_SEARCH_USED", "loan_request", null, "Busca contextual usada no pedido documental", null, null, new { query = q, results = result.Items.Count, suggestions = result.Suggestions }, ct);
            var payload = result.Items.Select(x => new
            {
                id = x.DocumentId,
                versionId = x.VersionId,
                code = x.DocumentId.ToString("N")[..8],
                title = x.Title,
                status = "Disponível para solicitação",
                createdAt = (DateTime?)null,
                type = x.DocumentType ?? x.ClassificationName ?? "Documento",
                folderPath = x.FolderName ?? "",
                score = x.Score,
                reasons = x.Reasons.Select(r => new { type = r.Reason, message = string.IsNullOrWhiteSpace(r.Evidence) ? r.Reason : $"{r.Reason}: {r.Evidence}", weight = r.Weight }),
                hasOcr = x.HasOcr,
                ocrSnippet = x.OcrSnippet,
                digitalAvailable = true,
                physicalAvailable = true,
                physicalLocation = x.FolderName ?? "Localização física a confirmar"
            }).ToList();
            return Json(new { success = true, items = payload, total = payload.Count, message = result.Message, suggestions = result.Suggestions, explanation = result.Intent.Explanation });
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

    // Rotina mutável: somente POST e antiforgery.
    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("Overdue/Run")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunOverdue(CancellationToken ct)
    {
        try
        {
            var updated = await _overdue.RunAsync(_user.TenantId, _user.UserId, ct);
            TempData["Ok"] = $"Rotina de vencidos concluída. Processados: {updated}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na rotina de vencidos. Tenant={TenantId}", _user.TenantId);
            TempData["Err"] = "Não foi possível executar a rotina de vencidos.";
        }
        return RedirectToAction(nameof(Overdue));
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("Overdue/Register")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RegisterOverdue(CancellationToken ct) => RunOverdue(ct);

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("Overdue/Collect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CollectSelected(Guid[] ids, CancellationToken ct)
    {
        var succeeded=0;
        foreach(var id in ids.Distinct().Take(500))
        {
            if (!await CanOperateLoanAsync(id,ct)) continue;
            if ((await _collections.CollectAsync(_user.TenantId,id,_user.UserId,null,ct)).IsSuccess) succeeded++;
        }
        TempData["Ok"]=$"Cobranças registradas: {succeeded}.";
        return RedirectToAction(nameof(Overdue));
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Collect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Collect(Guid id, string? message, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        var result = await _collections.CollectAsync(_user.TenantId, id, _user.UserId, message, ct);
        TempData[result.IsSuccess ? "Ok" : "Err"] = result.IsSuccess ? "Cobrança registrada." : result.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Renew")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(Guid id, DateTimeOffset? newDueAt, string reason, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        if (newDueAt is null || newDueAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(reason))
        { TempData["Err"] = "Nova data futura e justificativa são obrigatórias."; return RedirectToAction(nameof(Details), new { id }); }
        await using var conn = await _db.OpenAsync(ct); await using var tx = await conn.BeginTransactionAsync(ct);
        var before = await conn.QuerySingleOrDefaultAsync<(string Status, DateTimeOffset DueAt)>(new CommandDefinition("select status::text as Status,due_at as DueAt from ged.loan_request where tenant_id=@tenantId and id=@id and coalesce(reg_status,'A')='A' for update", new { tenantId=_user.TenantId,id },tx,cancellationToken:ct));
        if (string.IsNullOrEmpty(before.Status) || new[]{"RETURNED","REJECTED","CANCELLED","CANCELED"}.Contains(before.Status.ToUpperInvariant()))
        { await tx.RollbackAsync(ct); TempData["Err"]="Empréstimo encerrado não pode ser renovado."; return RedirectToAction(nameof(Details),new{id}); }
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.loan_request set previous_due_at=due_at,due_at=@due,sla_due_at=case when sla_due_at is null then null else @due end,renewed_at=now(),renewed_by=@userId,renewal_reason=@reason where tenant_id=@tenantId and id=@id;
insert into ged.loan_request_history(tenant_id,loan_request_id,old_status,new_status,action,user_id,user_name,reason,metadata_json,correlation_id,reg_status) values(@tenantId,@id,@status,@status,'LOAN_RENEWED',@userId,'Gestor',@reason,jsonb_build_object('previousDueAt',@oldDue,'newDueAt',@due),gen_random_uuid()::text,'A');
insert into ged.loan_request_message(tenant_id,loan_request_id,sender_user_id,sender_name,sender_role,message,message_type,is_internal) values(@tenantId,@id,@userId,'Gestor','MANAGER','Prazo renovado até '||to_char(@due,'DD/MM/YYYY HH24:MI')||'. Motivo: '||@reason,'RENEWAL',false);
""",new{tenantId=_user.TenantId,id,due=newDueAt,userId=_user.UserId,reason=reason.Trim(),status=before.Status,oldDue=before.DueAt},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        await _audit.WriteAsync(_user.TenantId,_user.UserId,"LOAN_RENEWED","loan_request",id,"Prazo de empréstimo renovado",null,null,new{previousDueAt=before.DueAt,newDueAt,reason},ct);
        TempData["Ok"]="Prazo renovado com rastreabilidade."; return RedirectToAction(nameof(Details),new{id});
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpGet("Reports")]
    public async Task<IActionResult> Reports([FromQuery] LoanReportFilter filter, string? format, CancellationToken ct)
    {
        var report=await _reports.RunAsync(_user.TenantId,_user.UserId,filter,ct);
        if (!string.Equals(format,"csv",StringComparison.OrdinalIgnoreCase)) return View(report);
        var csv=new StringBuilder("Protocolo;Solicitante;Setor;Status;Entrega;Solicitado;Vencimento;Dias atraso;Cobranças\n");
        foreach(var row in report.Rows) csv.AppendLine($"{row.ProtocolNo};{Csv(row.RequesterName)};{Csv(row.Sector)};{row.Status};{row.DeliveryMode};{row.RequestedAt:yyyy-MM-dd};{row.DueAt:yyyy-MM-dd};{row.DaysLate};{row.CollectionCount}");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),"text/csv","relatorio-emprestimos.csv");
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";


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
    {
        var sector = User.FindFirst("sector")?.Value ?? User.FindFirst("setor")?.Value;
        return View(new LoanCreateVM
        {
            DeliveryMode = "DIGITAL",
            Priority = "NORMAL",
            RequesterSectorName = sector,
            RequesterName = User.Identity?.Name ?? string.Empty
        });
    }

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

            TempData["Ok"] = "Solicitação enviada com sucesso. Um responsável analisará sua solicitação e responderá por aqui.";
            return RedirectToAction(nameof(Details), new { id = res.Value });
        }
        catch (Exception ex)
        {
            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogError(ex, "Loans.New POST failed. CorrelationId={CorrelationId}", correlationId);
            TempData["Err"] = $"Não foi possível registrar sua solicitação. Informe o suporte com o código: {correlationId}";
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
        if (string.IsNullOrWhiteSpace(notes)) { TempData["Err"] = "A observação da devolução é obrigatória."; return RedirectToAction(nameof(Details), new { id }); }
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


    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/Respond")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(Guid id, string message, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await WriteLoanMessageAsync(id, message, "ADMIN_RESPONSE", false, "TRIAGE", ct);
        TempData["Ok"] = "Resposta registrada para o solicitante.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/SetSla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSla(Guid id, int slaHours, DateTimeOffset? dueAt, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        var slaDue = dueAt?.UtcDateTime ?? DateTime.UtcNow.AddHours(Math.Clamp(slaHours <= 0 ? 24 : slaHours, 1, 720));
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.loan_request set sla_hours=@slaHours, sla_due_at=@slaDue, due_at=@slaDue, last_message_at=now() where tenant_id=@tenantId and id=@id", new { tenantId = _user.TenantId, id, slaHours, slaDue }, cancellationToken: ct));
        await WriteLoanMessageAsync(id, $"SLA definido para {slaDue:dd/MM/yyyy HH:mm} UTC.", "SLA", true, null, ct);
        TempData["Ok"] = "SLA atualizado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/RequestMoreInfo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestMoreInfo(Guid id, string message, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await WriteLoanMessageAsync(id, message, "NEEDS_INFO", false, "NEEDS_INFO", ct);
        TempData["Ok"] = "Solicitação de informações enviada.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansView)]
    [HttpPost("{id:guid}/RequesterReply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequesterReply(Guid id, string message, CancellationToken ct)
    {
        var scope = await _loanAccess.BuildLoanScopeAsync(_user.TenantId, _user.UserId, User, ct);
        if (await _service.GetDetailsAsync(_user.TenantId, id, _user.UserId, scope, ct) is null) return Forbid();
        await WriteLoanMessageAsync(id, message, "REQUESTER_REPLY", false, "TRIAGE", ct);
        TempData["Ok"] = "Complemento enviado.";
        return RedirectToAction(nameof(Details), new { id });
    }


    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpGet("{id:guid}/DocumentSearch")]
    public Task<IActionResult> DocumentSearch(Guid id, string? q, CancellationToken ct) => DocSearch(q, ct);

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/AssociateDocument")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssociateDocument(Guid id, AssociateLoanDocumentRequest request, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        var title = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select title from ged.document where tenant_id=@tenantId and id=@documentId and coalesce(reg_status,'A')='A'", new { tenantId = _user.TenantId, request.DocumentId }, cancellationToken: ct)) ?? request.DocumentId.ToString();
        var updated = await conn.ExecuteAsync(new CommandDefinition("""
update ged.loan_request_item
set matched_document_id=@documentId, matched_version_id=@versionId, document_id=coalesce(document_id,@documentId), document_version_id=coalesce(document_version_id,@versionId), match_score=@matchScore, match_reason=coalesce(@matchReason,'Associado manualmente pelo gestor'), notes=coalesce(@notes, notes), digital_available=true
where tenant_id=@tenantId and loan_request_id=@id and coalesce(reg_status,'A')='A' and (is_manual=true or matched_document_id is null);
""", new { tenantId = _user.TenantId, id, request.DocumentId, request.VersionId, request.MatchScore, request.MatchReason, request.Notes }, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition("insert into ged.loan_request_item(tenant_id, loan_request_id, document_id, document_version_id, matched_document_id, matched_version_id, match_score, match_reason, notes, is_manual, digital_available) values(@tenantId,@id,@documentId,@versionId,@documentId,@versionId,@matchScore,coalesce(@matchReason,'Associado manualmente pelo gestor'),@notes,false,true)", new { tenantId = _user.TenantId, id, request.DocumentId, request.VersionId, request.MatchScore, request.MatchReason, request.Notes }, cancellationToken: ct));
        await WriteLoanMessageAsync(id, $"Documento {title} associado ao pedido pelo gestor.", "DOCUMENT_ASSOCIATED", true, "TRIAGE", ct);
        await _audit.WriteAsync(_user.TenantId, _user.UserId, "LOAN_REQUEST_DOCUMENT_ASSOCIATED", "loan_request", id, "Documento associado ao pedido pelo gestor.", null, null, new { request.DocumentId, request.VersionId }, ct);
        TempData["Ok"] = "Documento associado ao pedido.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/RemoveAssociatedDocument")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAssociatedDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.loan_request_item set matched_document_id=null, matched_version_id=null, match_score=null, match_reason=null, digital_available=false where tenant_id=@tenantId and loan_request_id=@id and (matched_document_id=@documentId or document_id=@documentId)", new { tenantId = _user.TenantId, id, documentId }, cancellationToken: ct));
        await WriteLoanMessageAsync(id, "Documento removido do pedido pelo gestor.", "DOCUMENT_UNLINKED", true, null, ct);
        await _audit.WriteAsync(_user.TenantId, _user.UserId, "LOAN_REQUEST_DOCUMENT_UNLINKED", "loan_request", id, "Documento removido do pedido.", null, null, new { documentId }, ct);
        TempData["Ok"] = "Associação removida.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/LinkDocument")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkDocument(Guid id, Guid documentId, Guid? versionId, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.loan_request_item set matched_document_id=@documentId, matched_version_id=@versionId, document_id=coalesce(document_id,@documentId), match_reason='Vinculado pelo ADMIN' where tenant_id=@tenantId and loan_request_id=@id", new { tenantId = _user.TenantId, id, documentId, versionId }, cancellationToken: ct));
        await WriteLoanMessageAsync(id, "Documento vinculado pelo ADMIN.", "LINK_DOCUMENT", true, null, ct);
        TempData["Ok"] = "Documento vinculado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/GenerateSecureLink")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateSecureLink(Guid id, CreateSecureDocumentLinkRequest request, CancellationToken ct = default)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        request.LoanRequestId = id;
        request.AllowPreview = true;

        try
        {
            var result = await _secureLinks.CreateAsync(_user.TenantId, _user.UserId, request, ct);
            await _audit.WriteAsync(_user.TenantId, _user.UserId, "LOAN_REQUEST_DIGITAL_LINK_SENT", "loan_request", id, "Link digital enviado ao solicitante.", null, null, new { result.LinkId, request.DocumentId }, ct);
            TempData["Ok"] = $"Link seguro gerado: {result.PublicUrl}";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Err"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }


    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/RevokeSecureLink")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeSecureLink(Guid id, string? reason, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.secure_document_link set revoked_at=now(), revoked_by=@userId, revoke_reason=@reason where tenant_id=@tenantId and loan_request_id=@id and revoked_at is null", new { tenantId = _user.TenantId, id, userId = _user.UserId, reason }, cancellationToken: ct));
        await WriteLoanMessageAsync(id, "Link digital revogado." + (string.IsNullOrWhiteSpace(reason) ? string.Empty : " Motivo: " + reason), "DIGITAL_LINK_REVOKED", true, null, ct);
        TempData["Ok"] = "Link seguro revogado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/SendDigitalLink")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SendDigitalLink(Guid id, string? message, CancellationToken ct) => Respond(id, string.IsNullOrWhiteSpace(message) ? "Link digital seguro enviado ao solicitante." : message, ct);

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/SetPhysicalDelivery")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPhysicalDelivery(Guid id, string instructions, string? location, string? boxCode, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("update ged.loan_request set delivery_instructions=@instructions, physical_delivery_enabled=true, status='PREPARING_PHYSICAL', last_message_at=now() where tenant_id=@tenantId and id=@id; update ged.loan_request_item set physical_location=coalesce(@location, physical_location), box_code=coalesce(@boxCode, box_code), physical_available=true where tenant_id=@tenantId and loan_request_id=@id", new { tenantId = _user.TenantId, id, instructions, location, boxCode }, cancellationToken: ct));
        await WriteLoanMessageAsync(id, instructions, "PHYSICAL_LOCATION", false, "PREPARING_PHYSICAL", ct);
        TempData["Ok"] = "Entrega física configurada.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/MarkReadyForPickup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReadyForPickup(Guid id, string? notes, CancellationToken ct)
    {
        if (!await CanOperateLoanAsync(id, ct)) return Forbid();
        await WriteLoanMessageAsync(id, notes ?? "Documento físico disponível para retirada.", "READY_FOR_PICKUP", false, "WAITING_PICKUP", ct);
        TempData["Ok"] = "Pedido marcado como aguardando retirada.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.LoansManage)]
    [HttpPost("{id:guid}/MarkDelivered")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MarkDelivered(Guid id, string? notes, CancellationToken ct) => Deliver(id, notes, null, true, ct);

    private async Task WriteLoanMessageAsync(Guid id, string? message, string type, bool isInternal, string? status, CancellationToken ct)
    {
        message = string.IsNullOrWhiteSpace(message) ? "Atualização do pedido." : message.Trim();
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.loan_request_message(tenant_id, loan_request_id, sender_user_id, sender_name, sender_role, message, message_type, is_internal)
values(@tenantId,@id,@userId,@sender,@role,@message,@type,@isInternal);
update ged.loan_request set admin_response=case when @isInternal then admin_response else @message end, admin_response_at=case when @isInternal then admin_response_at else now() end, admin_response_by=case when @isInternal then admin_response_by else @userId end, last_message_at=now(), status=case when @status is null then status else @status::ged.loan_status end where tenant_id=@tenantId and id=@id;
""", new { tenantId = _user.TenantId, id, userId = _user.UserId, sender = _user.Email, role = string.Join(',', _user.Roles), message, type, isInternal, status }, cancellationToken: ct));
        _ = await _audit.WriteAsync(_user.TenantId, _user.UserId, type switch { "SLA" => "LOAN_REQUEST_SLA_SET", "DIGITAL_LINK" => "LOAN_REQUEST_DIGITAL_LINK_CREATED", "PHYSICAL_LOCATION" => "LOAN_REQUEST_PHYSICAL_LOCATION_SET", "NEEDS_INFO" => "LOAN_REQUEST_MORE_INFO_REQUESTED", _ => "LOAN_REQUEST_ADMIN_RESPONDED" }, "loan_request", id, message.Length > 250 ? message[..250] : message, null, null, new { type, status }, ct);
    }

    private static string Sha256Token(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

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



public sealed class AssociateLoanDocumentRequest
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public decimal? MatchScore { get; set; }
    public string? MatchReason { get; set; }
    public string? Notes { get; set; }
}
