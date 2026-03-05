using Dapper;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Users;

namespace InovaGed.Web.Controllers;

public class UserCertificatesController : Controller
{
    private readonly IDbConnectionFactory _db;

    public UserCertificatesController(IDbConnectionFactory db)
    {
        _db = db;
    }

    private Guid TenantId =>
        Guid.Parse(User.FindFirst("tenant_id")?.Value
        ?? "00000000-0000-0000-0000-000000000001");

    // LISTAR CERTIFICADOS DO USUÁRIO
    public async Task<IActionResult> Index(Guid userId)
    {
        await using var conn = await _db.OpenAsync(HttpContext.RequestAborted);

        var sql = """
        select
            id,
            user_id as UserId,
            cpf,
            thumbprint,
            subject_dn as SubjectDn,
            issuer_dn as IssuerDn,
            serial_number as SerialNumber,
            not_before as NotBefore,
            not_after as NotAfter,
            is_active as IsActive
        from ged.user_certificate
        where user_id=@userId
        and reg_status='A'
        order by reg_date desc
        """;

        var data = await conn.QueryAsync<UserCertificateDto>(sql, new { userId });

        ViewBag.UserId = userId;

        return View(data);
    }

    // FORM CREATE
    public IActionResult Create(Guid userId)
    {
        ViewBag.UserId = userId;
        return View();
    }

    // CREATE
    [HttpPost]
    public async Task<IActionResult> Create(UserCertificateDto dto)
    {
        dto.Cpf = (dto.Cpf ?? "").Trim();
        dto.Thumbprint = (dto.Thumbprint ?? "").Trim();

        if (dto.UserId == Guid.Empty)
        {
            TempData["Error"] = "Usuário inválido (UserId ausente).";
            return RedirectToAction(nameof(Index), new { userId = dto.UserId });
        }

        if (string.IsNullOrWhiteSpace(dto.Thumbprint))
        {
            TempData["Error"] = "Thumbprint é obrigatório.";
            ViewBag.UserId = dto.UserId;
            return View(dto);
        }

        if (string.IsNullOrWhiteSpace(dto.Cpf) || dto.Cpf.Length != 11)
        {
            TempData["Error"] = "CPF inválido. Informe 11 dígitos (somente números).";
            ViewBag.UserId = dto.UserId;
            return View(dto);
        }

        await using var conn = await _db.OpenAsync(HttpContext.RequestAborted);

        var sql = """
        insert into ged.user_certificate
        (
            tenant_id,
            user_id,
            cpf,
            thumbprint,
            subject_dn,
            issuer_dn,
            serial_number,
            not_before,
            not_after,
            is_active
        )
        values
        (
            @TenantId,
            @UserId,
            @Cpf,
            @Thumbprint,
            @SubjectDn,
            @IssuerDn,
            @SerialNumber,
            @NotBefore,
            @NotAfter,
            true
        )
        """;

        await conn.ExecuteAsync(sql, new
        {
            TenantId,
            dto.UserId,
            dto.Cpf,
            dto.Thumbprint,
            dto.SubjectDn,
            dto.IssuerDn,
            dto.SerialNumber,
            dto.NotBefore,
            dto.NotAfter
        });

        return RedirectToAction(nameof(Index), new { userId = dto.UserId });
    }

    // DESATIVAR
    public async Task<IActionResult> Disable(Guid id, Guid userId)
    {
        await using var conn = await _db.OpenAsync(HttpContext.RequestAborted);

        await conn.ExecuteAsync("""
            update ged.user_certificate
            set is_active=false
            where id=@id
        """, new { id });

        return RedirectToAction(nameof(Index), new { userId });
    }
}