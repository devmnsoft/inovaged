using InovaGed.Application.Identity;
using InovaGed.Application.Users;
using InovaGed.Infrastructure.Security;
using InovaGed.Web.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Users")]
public sealed class UsersController : Controller
{
    private readonly ILogger<UsersController> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAdminRepository _repo;
    private readonly IUserAdminQueries _queries;

    public UsersController(
        ILogger<UsersController> logger,
        ICurrentUser currentUser,
        IUserAdminRepository repo,
        IUserAdminQueries queries)
    {
        _logger = logger;
        _currentUser = currentUser;
        _repo = repo;
        _queries = queries;
    }

    // ========== INDEX ==========
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(string? q, bool? active, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        var res = await _queries.ListUsersAsync(tenantId, q, active, page, pageSize, ct);

        var vm = new UserListVM
        {
            Q = q,
            Active = active,
            Page = page <= 0 ? 1 : page,
            PageSize = pageSize,
            Total = res.Total,
            Items = res.Items.Select(x => new UserListVM.Row
            {
                Id = x.Id,
                Name = x.Name,
                Email = x.Email,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                Roles = (x.RolesCsv ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList()
            }).ToList()
        };

        ViewData["Title"] = "Usuários";
        ViewData["Subtitle"] = "Gerencie usuários, status e senhas";
        return View(vm);
    }

    // ========== CREATE ==========
    [HttpGet("Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var roles = await _repo.ListRolesAsync(tenantId, ct);

        var vm = new CreateUserVM
        {
            IsActive = true,
            AvailableRoles = roles.Select(r => new CreateUserVM.RoleItem { Id = r.Id, Name = r.Name }).ToList()
        };

        ViewData["Title"] = "Criar Usuário";
        ViewData["Subtitle"] = "Cadastro de novo acesso";
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserVM vm, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        async Task ReloadRoles()
        {
            var roles = await _repo.ListRolesAsync(tenantId, ct);
            vm.AvailableRoles = roles.Select(r => new CreateUserVM.RoleItem { Id = r.Id, Name = r.Name }).ToList();
        }

        if (!ModelState.IsValid)
        {
            await ReloadRoles();
            return View(vm);
        }

        try
        {
            var hash = Pbkdf2PasswordHasher.Hash(vm.Password);

            await _repo.CreateUserAsync(
                tenantId,
                vm.Name.Trim(),
                vm.Email.Trim(),
                hash,
                vm.IsActive,
                vm.SelectedRoleIds,
                ct);

            TempData["Success"] = "Usuário criado com sucesso.";
            return RedirectToAction("Index");
        }
        catch (PostgresException pex) when (pex.SqlState == "23505")
        {
            ModelState.AddModelError(nameof(vm.Email), "Já existe um usuário com este e-mail.");
            await ReloadRoles();
            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar usuário.");
            ModelState.AddModelError("", "Erro ao criar usuário. Verifique os logs.");
            await ReloadRoles();
            return View(vm);
        }
    }

    // ========== ATIVAR / INATIVAR ==========
    [HttpPost("ToggleActive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid id, bool makeActive, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        try
        {
            await _repo.SetActiveAsync(tenantId, id, makeActive, ct);
            TempData["Success"] = makeActive ? "Usuário ativado." : "Usuário inativado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao alterar status usuário {UserId}", id);
            TempData["Error"] = "Falha ao alterar status do usuário.";
        }

        return RedirectToAction("Index");
    }

    // ========== RESETAR SENHA ==========
    [HttpPost("ResetPassword")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;

        try
        {
            // senha temporária simples (pode trocar por UI de modal depois)
            var temp = "Inova@" + DateTime.UtcNow.ToString("yyyyMMdd") + "!";
            var hash = Pbkdf2PasswordHasher.Hash(temp);

            await _repo.ResetPasswordAsync(tenantId, id, hash, ct);

            TempData["Success"] = $"Senha redefinida. Senha temporária: {temp}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro reset senha usuário {UserId}", id);
            TempData["Error"] = "Falha ao resetar senha.";
        }

        return RedirectToAction("Index");
    }
}
