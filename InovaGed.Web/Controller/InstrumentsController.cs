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

    // ---------- POP ----------
    [HttpGet("Pop", Name = "PopIndex")]
    public async Task<IActionResult> Pop(CancellationToken ct)
    {
        var rows = await _popQ.ListAsync(_user.TenantId, ct);
        return View(rows);
    }

    [HttpPost("Pop/Create", Name = "PopCreate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePop([FromForm] PopProcedureCreateVM vm, CancellationToken ct)
    {
        if (vm == null)
        {
            TempData["Err"] = "Dados inválidos.";
            return RedirectToRoute("PopIndex");
        }

        if (string.IsNullOrWhiteSpace(vm.Code))
        {
            TempData["Err"] = "Informe o código do POP.";
            return RedirectToRoute("PopIndex");
        }

        if (string.IsNullOrWhiteSpace(vm.Title))
        {
            TempData["Err"] = "Informe o título do POP.";
            return RedirectToRoute("PopIndex");
        }

        if (string.IsNullOrWhiteSpace(vm.ContentMd))
        {
            TempData["Err"] = "Informe o conteúdo do POP.";
            return RedirectToRoute("PopIndex");
        }

        var res = await _popC.CreateAsync(_user.TenantId, _user.UserId, vm, ct);

        TempData[res.IsSuccess ? "Ok" : "Err"] =
            res.IsSuccess ? "POP criado." : (res.ErrorMessage ?? "Falha ao criar POP.");

        return RedirectToRoute("PopIndex");
    }

    [HttpPost("Pop/Update/{id:guid}", Name = "PopUpdate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePop(Guid id, [FromForm] PopProcedureUpdateVM vm, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            TempData["Err"] = "Id inválido.";
            return RedirectToRoute("PopIndex");
        }

        if (vm == null)
        {
            TempData["Err"] = "Dados inválidos.";
            return RedirectToRoute("PopIndex");
        }

        if (string.IsNullOrWhiteSpace(vm.Code))
        {
            TempData["Err"] = "Informe o código do POP.";
            return RedirectToRoute("PopIndex");
        }

        if (string.IsNullOrWhiteSpace(vm.Title))
        {
            TempData["Err"] = "Informe o título do POP.";
            return RedirectToRoute("PopIndex");
        }

        if (string.IsNullOrWhiteSpace(vm.ContentMd))
        {
            TempData["Err"] = "Informe o conteúdo do POP.";
            return RedirectToRoute("PopIndex");
        }

        var res = await _popC.UpdateAsync(_user.TenantId, id, _user.UserId, vm, ct);

        TempData[res.IsSuccess ? "Ok" : "Err"] =
            res.IsSuccess ? "POP atualizado." : (res.ErrorMessage ?? "Falha ao atualizar POP.");

        return RedirectToRoute("PopIndex");
    }

    

    [HttpGet("Pop/Versions/{procedureId:guid}", Name = "PopVersions")]
    public async Task<IActionResult> PopVersions(Guid procedureId, CancellationToken ct)
    {
        var vers = await _popQ.ListVersionsAsync(_user.TenantId, procedureId, ct);
        ViewBag.ProcedureId = procedureId;
        return View(vers);
    }

    [HttpPost("Pop/PublishVersion", Name = "PopPublishVersion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishPopVersion([FromForm] PublishPopVersionVM vm, CancellationToken ct)
    {
        var res = await _popC.PublishVersionAsync(_user.TenantId, _user.UserId, vm, ct);

        TempData[res.IsSuccess ? "Ok" : "Err"] =
            res.IsSuccess ? "Versão do POP publicada." : (res.ErrorMessage ?? "Falha ao publicar versão.");

        return RedirectToRoute("PopVersions", new { procedureId = vm.ProcedureId });
    }
}