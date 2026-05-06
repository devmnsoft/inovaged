using Dapper;
using InovaGed.Application.Common.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class SecurityAuditController : Controller
{
    private readonly IDbConnectionFactory _db;

    public SecurityAuditController(IDbConnectionFactory db)
    {
        _db = db;
    }

    // GET /SecurityAudit/AccessFailures  ()
    public async Task<IActionResult> AccessFailures(CancellationToken ct)
    {
        ViewData["Title"] = "Falhas de Acesso";
        ViewData["Subtitle"] = "Auditoria de bloqueios e desafios (401/403)";

        const string sql = @"
select
  occurred_at_utc as OccurredAtUtc,
  tenant_id       as TenantId,
  user_id         as UserId,
  user_name       as UserName,
  path            as Path,
  method          as Method,
  ip              as Ip,
  user_agent      as UserAgent,
  reason          as Reason,
  status_code     as StatusCode,
  notes           as Notes
from ged.access_failure
order by occurred_at_utc desc
limit 500;
";

        using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<AccessFailureRow>(sql)).AsList();

        return View(rows);
    }

    public sealed class AccessFailureRow
    {
        public DateTime OccurredAtUtc { get; set; }
        public Guid TenantId { get; set; }          // ✅ Guid (não TenantId value object)
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string Path { get; set; } = "";
        public string Method { get; set; } = "";
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }
        public string Reason { get; set; } = "";
        public int StatusCode { get; set; }
        public string? Notes { get; set; }
    }
}