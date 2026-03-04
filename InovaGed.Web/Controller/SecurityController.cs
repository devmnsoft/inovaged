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

    // GET /Security/RolePermissions?roleId=...
    [HttpGet]
    public async Task<IActionResult> RolePermissions(Guid roleId)
    {
        using var db = await OpenAsync();

        ViewBag.RoleId = roleId;

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

    // POST /Security/TogglePermission
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePermission(Guid roleId, string permissionCode, bool enable)
    {
        using var db = await OpenAsync();

        if (string.IsNullOrWhiteSpace(permissionCode))
            return BadRequest("permissionCode inválido.");

        if (enable)
        {
            await db.ExecuteAsync(@"
insert into ged.role_permission (tenant_id, role_id, permission_code)
values (@tid, @roleId, @code)
on conflict do nothing;", new { tid = TenantId, roleId, code = permissionCode });
        }
        else
        {
            await db.ExecuteAsync(@"
delete from ged.role_permission
where tenant_id=@tid and role_id=@roleId and permission_code=@code;", new { tid = TenantId, roleId, code = permissionCode });
        }

        return RedirectToAction(nameof(RolePermissions), new { roleId });
    }
}