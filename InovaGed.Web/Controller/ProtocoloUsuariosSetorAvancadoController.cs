using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Protocolo;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("Protocolo/UsuariosSetorAvancado")]
public sealed class ProtocoloUsuariosSetorAvancadoController : GedControllerBase
{
    public ProtocoloUsuariosSetorAvancadoController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    [HttpGet("BuscarUsuarios")]
    public async Task<IActionResult> BuscarUsuarios(string? q)
    {
        if (!PodeAdministrar()) return Forbid();
        using var db = await OpenAsync();

        var usuarios = new List<ProtocoloUsuarioBuscaItemVM>();
        var hasAspNetUsers = await db.ExecuteScalarAsync<bool>("select to_regclass('public.\"AspNetUsers\"') is not null;");
        var hasUsers = await db.ExecuteScalarAsync<bool>("select to_regclass('public.users') is not null;");

        if (hasAspNetUsers)
        {
            usuarios = (await db.QueryAsync<ProtocoloUsuarioBuscaItemVM>(@"
select ""Id""::uuid as Id, coalesce(""UserName"", ""Email"", ""Id"") as Nome, ""Email"" as Email
from public.""AspNetUsers""
where cast(@Q as text) is null or cast(@Q as text)='' or ""UserName"" ilike '%'||cast(@Q as text)||'%' or ""Email"" ilike '%'||cast(@Q as text)||'%'
order by Nome limit 50;", new { Q = q })).ToList();
        }
        else if (hasUsers)
        {
            usuarios = (await db.QueryAsync<ProtocoloUsuarioBuscaItemVM>(@"
select id as Id, coalesce(name, email, id::text) as Nome, email as Email
from public.users
where cast(@Q as text) is null or cast(@Q as text)='' or name ilike '%'||cast(@Q as text)||'%' or email ilike '%'||cast(@Q as text)||'%'
order by Nome limit 50;", new { Q = q })).ToList();
        }

        return View("~/Views/ProtocoloUsuariosSetorAvancado/BuscarUsuarios.cshtml", new ProtocoloUsuarioBuscaVM { Q = q, Usuarios = usuarios });
    }

    [HttpGet("Vincular")]
    public async Task<IActionResult> Vincular(Guid usuarioId, string? usuarioNome)
    {
        if (!PodeAdministrar()) return Forbid();
        using var db = await OpenAsync();
        return View("~/Views/ProtocoloUsuariosSetorAvancado/Vincular.cshtml", new ProtocoloUsuarioSetorPermissaoVM
        {
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome,
            Setores = await GetSetoresAsync(db)
        });
    }

    [HttpPost("Vincular")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Vincular(ProtocoloUsuarioSetorPermissaoVM vm)
    {
        if (!PodeAdministrar()) return Forbid();
        using var db = await OpenAsync();

        await db.ExecuteAsync(@"
insert into ged.protocolo_usuario_setor
(id, tenant_id, usuario_id, usuario_nome, setor_id, pode_visualizar, pode_receber, pode_tramitar, pode_anexar, pode_excluir_anexo, pode_decidir, pode_arquivar, ativo, created_at, reg_status)
values
(gen_random_uuid(), @TenantId, @UsuarioId, @UsuarioNome, @SetorId, @PodeVisualizar, @PodeReceber, @PodeTramitar, @PodeAnexar, @PodeExcluirAnexo, @PodeDecidir, @PodeArquivar, @Ativo, now(), 'A');", new
        {
            TenantId, vm.UsuarioId, vm.UsuarioNome, vm.SetorId, vm.PodeVisualizar, vm.PodeReceber, vm.PodeTramitar,
            vm.PodeAnexar, vm.PodeExcluirAnexo, vm.PodeDecidir, vm.PodeArquivar, vm.Ativo
        });

        TempData["ok"] = "Usuário vinculado ao setor.";
        return RedirectToAction("UsuariosSetor", "ProtocoloCadastros");
    }

    private async Task<List<SelectListItem>> GetSetoresAsync(System.Data.IDbConnection db)
    {
        var rows = await db.QueryAsync<(Guid Id, string Nome)>(
            "select id, nome from ged.protocolo_setor where tenant_id=@TenantId and ativo=true and reg_status='A' order by ordem, nome;",
            new { TenantId });

        return rows.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nome }).ToList();
    }

    private bool PodeAdministrar() => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Gestor) || User.IsInRole(AppRoles.Arquivista);
}
