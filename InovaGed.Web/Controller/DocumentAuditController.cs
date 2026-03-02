using Dapper;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = Policies.CanViewRetention)]
[Route("DocumentAudit")]
public sealed class DocumentAuditController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentContext _ctx;

    public DocumentAuditController(IDbConnectionFactory db, ICurrentContext ctx)
    {
        _db = db;
        _ctx = ctx;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid documentId, CancellationToken ct)
    {
        const string sql = @"
select event_at as EventAt, event_type as EventType, actor_email as ActorEmail, data as Data
from ged.document_audit
where tenant_id=@tenantId and document_id=@documentId
order by event_at desc
limit 200;
";
        await using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync(sql, new { tenantId = _ctx.TenantId, documentId })).ToList();

        ViewBag.DocumentId = documentId;
        return View(rows);
    }
}