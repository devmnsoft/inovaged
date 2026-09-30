using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ProtocolView)]
[Route("Protocols")]
public sealed class ProtocolsController : Controller
{
    private readonly ICurrentUser _user;
    private readonly IProtocolRequestService _service;
    private readonly IProtocolAccessService _access;
    private readonly IFileStorage _storage;
    private readonly ILogger<ProtocolsController> _logger;

    public ProtocolsController(ICurrentUser user, IProtocolRequestService service, IProtocolAccessService access, IFileStorage storage, ILogger<ProtocolsController> logger)
    { _user = user; _service = service; _access = access; _storage = storage; _logger = logger; }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(WorkQueue));

    [Authorize(Policy = AppPolicies.ProtocolRequest)]
    [HttpGet("Create")]
    public IActionResult Create(Guid? documentId) => RedirectToAction("New", "ProtocolRequests", new { documentId });

    [HttpGet("Inbox")]
    public IActionResult Inbox() => RedirectToAction(nameof(WorkQueue), new { onlyMine = true });

    [HttpGet("Outbox")]
    public IActionResult Outbox() => RedirectToAction("My", "ProtocolRequests");

    [HttpGet("Movements")]
    public IActionResult Movements() => RedirectToAction(nameof(WorkQueue), new { showAll = true });

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpGet("WorkQueue")]
    public async Task<IActionResult> WorkQueue(ProtocolWorkQueueFilter filter, CancellationToken ct)
    {
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        try
        {
            if (scope.IsAdministradorOphir && !scope.SectorId.HasValue && string.IsNullOrWhiteSpace(scope.SectorName))
                TempData["Error"] = "Seu usuário não possui setor vinculado. Configure o setor para visualizar solicitações.";
            var rows = await _service.ListWorkQueueAsync(_user.TenantId, _user.UserId, scope, filter ?? new(), ct);
            return View(new ProtocolWorkQueueVm { Filter = filter ?? new(), Rows = rows.ToList() });
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro ao carregar fila de protocolos.");
            TempData["Error"] = "Não foi possível carregar a fila de protocolos. Verifique o status do banco e as migrations.";
            return View(new ProtocolWorkQueueVm { Filter = filter ?? new(), Rows = new List<ProtocolRequestRowVm>() });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var vm = await _service.GetDetailsAsync(_user.TenantId, id, _user.UserId, scope, ct);
        if (vm is null) return NotFound();
        return View(vm);
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/Assume")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assume(Guid id, string? notes, CancellationToken ct)
    {
        if (!await _access.CanManageAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var res = await _service.AssumeAsync(_user.TenantId, id, _user.UserId, notes, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Protocolo assumido." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/{actionName:regex(^(Approve|ReturnForAdjustment|Reject|Finish)$)}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Critical(Guid id, string actionName, string reason, string? internalNotes, CancellationToken ct)
    {
        if (!await _access.CanManageAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var res = actionName switch
        {
            "Approve" => await _service.ApproveAsync(_user.TenantId, id, _user.UserId, reason, internalNotes, ct),
            "ReturnForAdjustment" => await _service.ReturnForAdjustmentAsync(_user.TenantId, id, _user.UserId, reason, internalNotes, ct),
            "Reject" => await _service.RejectAsync(_user.TenantId, id, _user.UserId, reason, internalNotes, ct),
            "Finish" => await _service.FinishAsync(_user.TenantId, id, _user.UserId, reason, internalNotes, ct),
            _ => InovaGed.Domain.Primitives.Result.Fail("ACTION", "Ação inválida.")
        };
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Ação registrada com sucesso." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:guid}/RespondAdjustment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondAdjustment(Guid id, string response, List<IFormFile>? attachments, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();

        // Validação prévia dos arquivos antes da transição irreversível de estado (nomes sanitizados na gravação).
        var validFiles = (attachments ?? new List<IFormFile>()).Where(f => f is not null && f.Length > 0).ToList();
        foreach (var file in validFiles)
        {
            var safe = Common.ProtocolAttachmentSaver.SanitizeFileName(file.FileName);
            if (!string.Equals(safe, Path.GetFileName(file.FileName ?? string.Empty), StringComparison.Ordinal))
                _logger.LogInformation("RespondAdjustment {Id}: nome do anexo será ajustado: '{Orig}' -> '{Safe}'.", id, file.FileName, safe);
        }

        var res = await _service.RespondAdjustmentAsync(_user.TenantId, id, _user.UserId, response, ct);
        var fileErrors = res.IsSuccess
            ? await Common.ProtocolAttachmentSaver.SaveAttachmentsAsync(_storage, _service, _user.TenantId, id, _user.UserId, validFiles, _logger, ct)
            : new List<Common.ProtocolFileResult>();

        if (!res.IsSuccess)
        {
            TempData["Err"] = res.ErrorMessage;
        }
        else if (fileErrors.Count > 0)
        {
            TempData["Err"] = ProtocolCustodyRules.AttachmentSummary(validFiles.Count, validFiles.Count - fileErrors.Count, fileErrors.Count);
            TempData["FileErrs"] = fileErrors;
        }
        else
        {
            TempData["Ok"] = "Ajuste respondido.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/CreateLoan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLoan(Guid id, CancellationToken ct)
    {
        if (!await _access.CanManageAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var res = await _service.CreateLoanAsync(_user.TenantId, id, _user.UserId, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Solicitação de empréstimo/documento gerada." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/Forward")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Forward(Guid id, ProtocolForwardCommand command, CancellationToken ct)
    {
        if (!await _access.CanManageAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        command.ProtocolId = id;
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        command.IsAdmin = scope.CanSeeAll;
        var res = await _service.ForwardAsync(_user.TenantId, _user.UserId, command, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Encaminhado. O destino ainda precisa receber." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/Receive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(Guid id, Guid? movementId, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var res = await _service.ReceiveMovementAsync(_user.TenantId, id, movementId, _user.UserId, scope.CanSeeAll, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Recebimento confirmado." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/ReturnCustody")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnCustody(Guid id, string reason, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var res = await _service.ReturnCustodyAsync(_user.TenantId, id, _user.UserId, scope.CanSeeAll, reason, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? res.ErrorMessage ?? "Devolução registrada." : res.ErrorMessage;
        if (res.IsSuccess) TempData["Ok"] = "Devolução pendente de confirmação. Isto não é devolução para ajuste.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/ConfirmReturn")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReturn(Guid id, Guid? movementId, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var res = await _service.ConfirmReturnAsync(_user.TenantId, id, movementId, _user.UserId, scope.CanSeeAll, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Retorno confirmado." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/Reverse")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(Guid id, string justification, CancellationToken ct)
    {
        if (!await _access.CanViewAsync(_user.TenantId, id, _user.UserId, User, ct)) return Forbid();
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var res = await _service.ReverseLastAsync(_user.TenantId, id, _user.UserId, scope.CanSeeAll, justification, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Estorno registrado. O evento original permanece no histórico." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("{id:guid}/Reopen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(Guid id, string justification, CancellationToken ct)
    {
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        if (!scope.CanSeeAll) return Forbid();
        var res = await _service.ReopenAsync(_user.TenantId, id, _user.UserId, true, justification, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Processo reaberto. Documentos e temporalidade não foram alterados." : res.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpPost("ReceiveMany")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceiveMany(Guid[] ids, CancellationToken ct)
    {
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var outcome = await _service.ReceiveManyAsync(_user.TenantId, _user.UserId, scope.CanSeeAll, ids ?? Array.Empty<Guid>(), ct);
        TempData[outcome.Partial || !outcome.AnyApplied ? "Err" : "Ok"] = outcome.Message;
        return RedirectToAction(nameof(WorkQueue), new { Queue = "receber" });
    }
}
