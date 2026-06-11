using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.ProtocolView)]
[Route("Protocols")]
public sealed class ProtocolsController : Controller
{
    private readonly ICurrentUser _user;
    private readonly IProtocolRequestService _service;
    private readonly IProtocolAccessService _access;
    private readonly IFileStorage _storage;

    public ProtocolsController(ICurrentUser user, IProtocolRequestService service, IProtocolAccessService access, IFileStorage storage)
    { _user = user; _service = service; _access = access; _storage = storage; }

    [Authorize(Policy = AppPolicies.ProtocolManage)]
    [HttpGet("WorkQueue")]
    public async Task<IActionResult> WorkQueue(ProtocolWorkQueueFilter filter, CancellationToken ct)
    {
        var scope = await _access.BuildScopeAsync(_user.TenantId, _user.UserId, User, ct);
        var rows = await _service.ListWorkQueueAsync(_user.TenantId, _user.UserId, scope, filter ?? new(), ct);
        return View(new ProtocolWorkQueueVm { Filter = filter ?? new(), Rows = rows.ToList() });
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
        var res = await _service.RespondAdjustmentAsync(_user.TenantId, id, _user.UserId, response, ct);
        if (res.IsSuccess && attachments is not null)
        {
            foreach (var file in attachments.Where(f => f.Length > 0))
            {
                var safe = Path.GetFileName(file.FileName);
                var path = $"protocols/{_user.TenantId:N}/{id:N}/{Guid.NewGuid():N}_{safe}";
                await using var stream = file.OpenReadStream();
                await _storage.SaveDerivedAsync(path, stream, file.ContentType ?? "application/octet-stream", ct);
                await _service.AddAttachmentAsync(_user.TenantId, id, _user.UserId, safe, file.ContentType, file.Length, path, ct);
            }
        }
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Ajuste respondido." : res.ErrorMessage;
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
}
