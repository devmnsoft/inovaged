using InovaGed.Application.Identity;
using InovaGed.Application.Workflow;
using InovaGed.Domain.Workflow;
using InovaGed.Web.Extensions;
using InovaGed.Web.Models.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class WorkflowController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkflowQueries _q;
    private readonly IWorkflowCommands _c;
    private readonly ILogger<WorkflowController> _logger;

    public WorkflowController(
        ICurrentUser currentUser,
        IWorkflowQueries queries,
        IWorkflowCommands commands,
        ILogger<WorkflowController> logger)
    {
        _currentUser = currentUser;
        _q = queries;
        _c = commands;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(Guid? id, string? q, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var vm = new WorkflowIndexVM
        {
            Workflows = await _q.ListDefinitionsAsync(tenantId, q, ct)
        };

        if (id.HasValue)
        {
            vm.Selected = await _q.GetDefinitionAsync(tenantId, id.Value, ct);
            if (vm.Selected != null)
            {
                vm.Stages = await _q.ListStagesAsync(tenantId, id.Value, ct);
                vm.Transitions = await _q.ListTransitionsAsync(tenantId, id.Value, ct);
            }
        }

        ViewBag.Q = q;
        return View(vm);
    }

    // ======== Workflow Definition ========

    [HttpGet]
    public IActionResult Novo()
    {
        return View(new WorkflowDefinitionFormVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Novo(WorkflowDefinitionFormVM vm, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) return View(vm);

            var result = await _c.CreateDefinitionAsync(_currentUser.TenantId,
                new CreateWorkflowDefinitionCommand
                {
                    Code = vm.Code!,
                    Name = vm.Name!,
                    Description = vm.Description
                },
                _currentUser.UserId,
                ct);

            if (!result.Success)
            {
                TempData["erro"] = result.GetMensagem() ?? "Não foi possível criar o workflow.";
                return View(vm);
            }

            TempData["ok"] = "Workflow criado com sucesso.";
            return RedirectToAction(nameof(Index), new { id = result.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar workflow.");
            TempData["erro"] = "Erro inesperado ao criar workflow.";
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Editar(Guid id, CancellationToken ct)
    {
        var w = await _q.GetDefinitionAsync(_currentUser.TenantId, id, ct);
        if (w is null)
        {
            TempData["erro"] = "Workflow não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        return View(new WorkflowDefinitionFormVM
        {
            Id = w.Id,
            Code = w.Code,
            Name = w.Name,
            Description = w.Description,
            IsActive = w.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(WorkflowDefinitionFormVM vm, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) return View(vm);

            var result = await _c.UpdateDefinitionAsync(_currentUser.TenantId,
                new UpdateWorkflowDefinitionCommand
                {
                    Id = vm.Id!.Value,
                    Code = vm.Code!,
                    Name = vm.Name!,
                    Description = vm.Description,
                    IsActive = vm.IsActive
                },
                _currentUser.UserId,
                ct);

            TempData[result.Success ? "ok" : "erro"] = result.Success
                ? "Workflow atualizado."
                : (result.GetMensagem() ?? "Não foi possível atualizar.");

            return RedirectToAction(nameof(Index), new { id = vm.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao editar workflow.");
            TempData["erro"] = "Erro inesperado ao editar workflow.";
            return View(vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        var result = await _c.DeactivateDefinitionAsync(_currentUser.TenantId, id, _currentUser.UserId, ct);

        TempData[result.Success ? "ok" : "erro"] = result.Success
            ? "Workflow desativado."
            : (result.GetMensagem() ?? "Não foi possível desativar.");

        return RedirectToAction(nameof(Index), new { id });
    }

    // ======== Stage ========

    [HttpGet]
    public IActionResult NovaEtapa(Guid workflowId)
    {
        return View(new WorkflowStageFormVM { WorkflowId = workflowId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NovaEtapa(WorkflowStageFormVM vm, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) return View(vm);

            var result = await _c.CreateStageAsync(_currentUser.TenantId,
                new CreateWorkflowStageCommand
                {
                    WorkflowId = vm.WorkflowId,
                    Code = vm.Code!,
                    Name = vm.Name!,
                    SortOrder = vm.SortOrder,
                    IsStart = vm.IsStart,
                    IsFinal = vm.IsFinal,
                    RequiredRole = vm.RequiredRole
                },
                _currentUser.UserId,
                ct);

            TempData[result.Success ? "ok" : "erro"] = result.Success
                ? "Etapa criada."
                : (result.GetMensagem() ?? "Não foi possível criar a etapa.");

            return RedirectToAction(nameof(Index), new { id = vm.WorkflowId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar etapa.");
            TempData["erro"] = "Erro inesperado ao criar etapa.";
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditarEtapa(Guid id, Guid workflowId, CancellationToken ct)
    {
        var stages = await _q.ListStagesAsync(_currentUser.TenantId, workflowId, ct);
        var s = stages.FirstOrDefault(x => x.Id == id);
        if (s is null)
        {
            TempData["erro"] = "Etapa não encontrada.";
            return RedirectToAction(nameof(Index), new { id = workflowId });
        }

        return View(new WorkflowStageFormVM
        {
            Id = s.Id,
            WorkflowId = s.WorkflowId,
            Code = s.Code,
            Name = s.Name,
            SortOrder = s.SortOrder,
            IsStart = s.IsStart,
            IsFinal = s.IsFinal,
            RequiredRole = s.RequiredRole
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarEtapa(WorkflowStageFormVM vm, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) return View(vm);

            var result = await _c.UpdateStageAsync(_currentUser.TenantId,
                new UpdateWorkflowStageCommand
                {
                    Id = vm.Id!.Value,
                    WorkflowId = vm.WorkflowId,
                    Code = vm.Code!,
                    Name = vm.Name!,
                    SortOrder = vm.SortOrder,
                    IsStart = vm.IsStart,
                    IsFinal = vm.IsFinal,
                    RequiredRole = vm.RequiredRole
                },
                _currentUser.UserId,
                ct);

            TempData[result.Success ? "ok" : "erro"] = result.Success
                ? "Etapa atualizada."
                : (result.GetMensagem() ?? "Não foi possível atualizar.");

            return RedirectToAction(nameof(Index), new { id = vm.WorkflowId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao editar etapa.");
            TempData["erro"] = "Erro inesperado ao editar etapa.";
            return View(vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirEtapa(Guid id, Guid workflowId, CancellationToken ct)
    {
        var result = await _c.DeleteStageAsync(_currentUser.TenantId, id, ct);

        TempData[result.Success ? "ok" : "erro"] = result.Success
            ? "Etapa excluída."
            : (result.GetMensagem() ?? "Não foi possível excluir.");

        return RedirectToAction(nameof(Index), new { id = workflowId });
    }

    // ======== Transition ========

    [HttpGet]
    public async Task<IActionResult> NovaTransicao(Guid workflowId, CancellationToken ct)
    {
        var stages = await _q.ListStagesAsync(_currentUser.TenantId, workflowId, ct);

        ViewBag.Stages = stages;
        return View(new WorkflowTransitionFormVM { WorkflowId = workflowId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NovaTransicao(WorkflowTransitionFormVM vm, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Stages = await _q.ListStagesAsync(_currentUser.TenantId, vm.WorkflowId, ct);
                return View(vm);
            }

            var result = await _c.CreateTransitionAsync(_currentUser.TenantId,
                new CreateWorkflowTransitionCommand
                {
                    WorkflowId = vm.WorkflowId,
                    FromStageId = vm.FromStageId,
                    ToStageId = vm.ToStageId,
                    Name = vm.Name!,
                    RequiresReason = vm.RequiresReason
                },
                _currentUser.UserId,
                ct);

            TempData[result.Success ? "ok" : "erro"] = result.Success
                ? "Transição criada."
                : (result.GetMensagem() ?? "Não foi possível criar a transição.");

            return RedirectToAction(nameof(Index), new { id = vm.WorkflowId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar transição.");
            TempData["erro"] = "Erro inesperado ao criar transição.";
            ViewBag.Stages = await _q.ListStagesAsync(_currentUser.TenantId, vm.WorkflowId, ct);
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditarTransicao(Guid id, Guid workflowId, CancellationToken ct)
    {
        var transitions = await _q.ListTransitionsAsync(_currentUser.TenantId, workflowId, ct);
        var t = transitions.FirstOrDefault(x => x.Id == id);
        if (t is null)
        {
            TempData["erro"] = "Transição não encontrada.";
            return RedirectToAction(nameof(Index), new { id = workflowId });
        }

        ViewBag.Stages = await _q.ListStagesAsync(_currentUser.TenantId, workflowId, ct);

        return View(new WorkflowTransitionFormVM
        {
            Id = t.Id,
            WorkflowId = t.WorkflowId,
            FromStageId = t.FromStageId,
            ToStageId = t.ToStageId,
            Name = t.Name,
            RequiresReason = t.RequiresReason
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarTransicao(WorkflowTransitionFormVM vm, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Stages = await _q.ListStagesAsync(_currentUser.TenantId, vm.WorkflowId, ct);
                return View(vm);
            }

            var result = await _c.UpdateTransitionAsync(_currentUser.TenantId,
                new UpdateWorkflowTransitionCommand
                {
                    Id = vm.Id!.Value,
                    WorkflowId = vm.WorkflowId,
                    FromStageId = vm.FromStageId,
                    ToStageId = vm.ToStageId,
                    Name = vm.Name!,
                    RequiresReason = vm.RequiresReason
                },
                _currentUser.UserId,
                ct);

            TempData[result.Success ? "ok" : "erro"] = result.Success
                ? "Transição atualizada."
                : (result.GetMensagem() ?? "Não foi possível atualizar.");

            return RedirectToAction(nameof(Index), new { id = vm.WorkflowId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao editar transição.");
            TempData["erro"] = "Erro inesperado ao editar transição.";
            ViewBag.Stages = await _q.ListStagesAsync(_currentUser.TenantId, vm.WorkflowId, ct);
            return View(vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirTransicao(Guid id, Guid workflowId, CancellationToken ct)
    {
        var result = await _c.DeleteTransitionAsync(_currentUser.TenantId, id, ct);

        TempData[result.Success ? "ok" : "erro"] = result.Success
            ? "Transição excluída."
            : (result.GetMensagem() ?? "Não foi possível excluir.");

        return RedirectToAction(nameof(Index), new { id = workflowId });
    }
}
