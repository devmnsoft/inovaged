using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Application.Identity;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class InstrumentsController : Controller
{
    private readonly ICurrentUser _user;
    private readonly IClassificationPlanQueries _pcdQ;
    private readonly IClassificationPlanCommands _pcdC;
    private readonly IPopProcedureQueries _popQ;
    private readonly IPopProcedureCommands _popC;

    public InstrumentsController(
        ICurrentUser user,
        IClassificationPlanQueries pcdQ,
        IClassificationPlanCommands pcdC,
        IPopProcedureQueries popQ,
        IPopProcedureCommands popC)
    {
        _user = user;
        _pcdQ = pcdQ;
        _pcdC = pcdC;
        _popQ = popQ;
        _popC = popC;
    }

    // ---------- PCD/TTD ----------
    [HttpGet("ClassificationPlan")]
    public async Task<IActionResult> ClassificationPlan(CancellationToken ct)
    {
        var rows = await _pcdQ.ListAsync(_user.TenantId, ct);
        return View(rows);
    }

    [HttpPost("ClassificationPlan/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClassificationPlan(ClassificationPlanCreateVM vm, CancellationToken ct)
    {
        var res = await _pcdC.CreateAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Classe criada." : res.ErrorMessage;
        return RedirectToAction(nameof(ClassificationPlan));
    }

    [HttpPost("ClassificationPlan/Update/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateClassificationPlan(Guid id, ClassificationPlanUpdateVM vm, CancellationToken ct)
    {
        var res = await _pcdC.UpdateAsync(_user.TenantId, id, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Classe atualizada." : res.ErrorMessage;
        return RedirectToAction(nameof(ClassificationPlan));
    }

    [HttpPost("ClassificationPlan/Move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveClassificationPlan(ClassificationPlanMoveVM vm, CancellationToken ct)
    {
        var res = await _pcdC.MoveAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Movimentação executada." : res.ErrorMessage;
        return RedirectToAction(nameof(ClassificationPlan));
    }

    [HttpGet("ClassificationPlan/Versions")]
    public async Task<IActionResult> ClassificationPlanVersions(CancellationToken ct)
    {
        var vers = await _pcdQ.ListVersionsAsync(_user.TenantId, ct);
        return View(vers);
    }

    [HttpPost("ClassificationPlan/PublishVersion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishClassificationPlanVersion(PublishClassificationPlanVersionVM vm, CancellationToken ct)
    {
        var res = await _pcdC.PublishVersionAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Versão publicada." : res.ErrorMessage;
        return RedirectToAction(nameof(ClassificationPlanVersions));
    }

    [HttpGet("ClassificationPlan/ExportCurrent")]
    public async Task<IActionResult> ExportCurrent([FromQuery] Guid? rootId, CancellationToken ct)
    {
        var csv = await _pcdQ.ExportCurrentCsvAsync(_user.TenantId, rootId, ct);
        var fileName = rootId.HasValue ? "pcd_ttd_classe.csv" : "pcd_ttd_inteiro.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    [HttpGet("ClassificationPlan/ExportVersion/{versionId:guid}")]
    public async Task<IActionResult> ExportVersion(Guid versionId, CancellationToken ct)
    {
        var csv = await _pcdQ.ExportVersionCsvAsync(_user.TenantId, versionId, ct);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "pcd_ttd_versao.csv");
    }

    // ---------- POP ----------
    [HttpGet("Pop")]
    public async Task<IActionResult> Pop(CancellationToken ct)
    {
        var rows = await _popQ.ListAsync(_user.TenantId, ct);
        return View(rows);
    }

    [HttpPost("Pop/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePop(PopProcedureCreateVM vm, CancellationToken ct)
    {
        var res = await _popC.CreateAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "POP criado." : res.ErrorMessage;
        return RedirectToAction(nameof(Pop));
    }

    [HttpPost("Pop/Update/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePop(Guid id, PopProcedureUpdateVM vm, CancellationToken ct)
    {
        var res = await _popC.UpdateAsync(_user.TenantId, id, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "POP atualizado." : res.ErrorMessage;
        return RedirectToAction(nameof(Pop));
    }

    [HttpGet("Pop/Versions/{procedureId:guid}")]
    public async Task<IActionResult> PopVersions(Guid procedureId, CancellationToken ct)
    {
        var vers = await _popQ.ListVersionsAsync(_user.TenantId, procedureId, ct);
        ViewBag.ProcedureId = procedureId;
        return View(vers);
    }

    [HttpPost("Pop/PublishVersion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishPopVersion(PublishPopVersionVM vm, CancellationToken ct)
    {
        var res = await _popC.PublishVersionAsync(_user.TenantId, _user.UserId, vm, ct);
        TempData[res.IsSuccess ? "Ok" : "Err"] = res.IsSuccess ? "Versão do POP publicada." : res.ErrorMessage;
        return RedirectToAction(nameof(PopVersions), new { procedureId = vm.ProcedureId });
    }
}