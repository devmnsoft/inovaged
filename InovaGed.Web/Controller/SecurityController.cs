using Dapper;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;

namespace InovaGed.Web.Controllers;

public class SecurityController : GedControllerBase
{
    public SecurityController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    // GET /Security/Roles
    [HttpGet]
    public async Task<IActionResult> Roles()
    {
        using var db = await OpenAsync();

        var roles = await db.QueryAsync(@"
select id, name, normalized_name, created_at
from ged.app_role
where tenant_id = @tid
order by name;", new { tid = TenantId });

        return View(roles);
    }

    // =========================
    // CREATE ROLE
    // =========================
    [HttpGet]
    public IActionResult RoleCreate()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleCreate(RoleEditVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "Informe o nome.");

        if (!ModelState.IsValid)
            return View(vm);

        var name = vm.Name.Trim();
        var normalized = NormalizeRoleName(vm.NormalizedName, name);

        using var db = await OpenAsync();

        // evita duplicidade por tenant
        var exists = await db.ExecuteScalarAsync<int>(@"
select count(1)
from ged.app_role
where tenant_id=@tid and normalized_name=@n;", new { tid = TenantId, n = normalized });

        if (exists > 0)
        {
            ModelState.AddModelError("", "Já existe um perfil com esse nome (normalizado) para este tenant.");
            return View(vm);
        }

        await db.ExecuteAsync(@"
insert into ged.app_role (id, tenant_id, name, normalized_name, created_at)
values (gen_random_uuid(), @tid, @name, @normalized, now());",
            new { tid = TenantId, name, normalized });

        return RedirectToAction(nameof(Roles));
    }

    // =========================
    // EDIT ROLE
    // =========================
    [HttpGet]
    public async Task<IActionResult> RoleEdit(Guid id)
    {
        using var db = await OpenAsync();

        var role = await db.QuerySingleOrDefaultAsync(@"
select id, name, normalized_name
from ged.app_role
where tenant_id=@tid and id=@id;", new { tid = TenantId, id });

        if (role is null) return NotFound();

        var vm = new RoleEditVm
        {
            Id = (Guid)role.id,
            Name = (string)role.name,
            NormalizedName = (string)role.normalized_name
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleEdit(RoleEditVm vm)
    {
        if (vm.Id == Guid.Empty) return BadRequest("Id inválido.");
        if (string.IsNullOrWhiteSpace(vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "Informe o nome.");

        if (!ModelState.IsValid)
            return View(vm);

        var name = vm.Name.Trim();
        var normalized = NormalizeRoleName(vm.NormalizedName, name);

        using var db = await OpenAsync();

        // garante que existe
        var ok = await db.ExecuteScalarAsync<int>(@"
select count(1)
from ged.app_role
where tenant_id=@tid and id=@id;", new { tid = TenantId, id = vm.Id });

        if (ok == 0) return NotFound();

        // evita duplicidade (outro role)
        var dup = await db.ExecuteScalarAsync<int>(@"
select count(1)
from ged.app_role
where tenant_id=@tid and normalized_name=@n and id<>@id;", new { tid = TenantId, n = normalized, id = vm.Id });

        if (dup > 0)
        {
            ModelState.AddModelError("", "Já existe outro perfil com esse nome (normalizado).");
            return View(vm);
        }

        await db.ExecuteAsync(@"
update ged.app_role
set name=@name, normalized_name=@normalized
where tenant_id=@tid and id=@id;",
            new { tid = TenantId, id = vm.Id, name, normalized });

        return RedirectToAction(nameof(Roles));
    }

    // =========================
    // DELETE ROLE (simples)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleDelete(Guid id)
    {
        if (id == Guid.Empty) return BadRequest("Id inválido.");

        using var db = await OpenAsync();

        // remove permissões primeiro (pra evitar FK, se existir)
        await db.ExecuteAsync(@"
delete from ged.role_permission
where tenant_id=@tid and role_id=@id;", new { tid = TenantId, id });

        await db.ExecuteAsync(@"
delete from ged.app_role
where tenant_id=@tid and id=@id;", new { tid = TenantId, id });

        return RedirectToAction(nameof(Roles));
    }

    // =========================
    // PERMISSIONS
    // =========================
    [HttpGet]
    public async Task<IActionResult> RolePermissions(Guid roleId)
    {
        using var db = await OpenAsync();

        // carrega o role (pra mostrar o nome no topo)
        var role = await db.QuerySingleOrDefaultAsync(@"
select id, name
from ged.app_role
where tenant_id=@tid and id=@roleId;", new { tid = TenantId, roleId });

        if (role is null) return NotFound();

        ViewBag.RoleId = roleId;
        ViewBag.RoleName = (string)role.name;

        var rows = await db.QueryAsync(@"
select
  p.code as code,
  p.name as name,
  (rp.permission_code is not null) as enabled
from ged.permission p
left join ged.role_permission rp
  on rp.tenant_id = @tid
 and rp.role_id   = @roleId
 and rp.permission_code = p.code
where p.code is not null
order by p.code;", new { tid = TenantId, roleId });

        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePermission(Guid roleId, string permissionCode, bool enable)
    {
        using var db = await OpenAsync();

        if (roleId == Guid.Empty) return BadRequest("roleId inválido.");
        if (string.IsNullOrWhiteSpace(permissionCode))
            return BadRequest("permissionCode inválido.");

        // garante que role existe no tenant
        var roleExists = await db.ExecuteScalarAsync<int>(@"
select count(1)
from ged.app_role
where tenant_id=@tid and id=@roleId;", new { tid = TenantId, roleId });

        if (roleExists == 0) return NotFound("Perfil não encontrado.");

        if (enable)
        {
            await db.ExecuteAsync(@"
insert into ged.role_permission (tenant_id, role_id, permission_code)
values (@tid, @roleId, @code)
on conflict do nothing;", new { tid = TenantId, roleId, code = permissionCode.Trim() });
        }
        else
        {
            await db.ExecuteAsync(@"
delete from ged.role_permission
where tenant_id=@tid and role_id=@roleId and permission_code=@code;",
                new { tid = TenantId, roleId, code = permissionCode.Trim() });
        }

        return RedirectToAction(nameof(RolePermissions), new { roleId });
    }

    private static string NormalizeRoleName(string? normalizedName, string name)
        => string.IsNullOrWhiteSpace(normalizedName)
            ? name.Trim().ToUpperInvariant().Replace("  ", " ")
            : normalizedName.Trim().ToUpperInvariant().Replace("  ", " ");
}

public sealed class RoleEditVm
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? NormalizedName { get; set; }
}