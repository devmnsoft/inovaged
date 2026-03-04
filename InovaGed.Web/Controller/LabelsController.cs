using Dapper;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;

namespace InovaGed.Web.Controllers;

public class LabelsController : GedControllerBase
{
    public LabelsController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    // GET /Labels/Boxes
    [HttpGet]
    public async Task<IActionResult> Boxes()
    {
        using var db = await OpenAsync();

        var rows = await db.QueryAsync(@"
select id, box_no, label_code, notes, reg_date, reg_status
from ged.box
where tenant_id=@tid and reg_status='A'
order by box_no;", new { tid = TenantId });

        return View(rows);
    }

    // GET /Labels/Documents
    [HttpGet]
    public async Task<IActionResult> Documents()
    {
        using var db = await OpenAsync();

        var rows = await db.QueryAsync(@"
select id, code, title, status, created_at
from ged.document
where tenant_id=@tid
order by created_at desc
limit 200;", new { tid = TenantId });

        return View(rows);
    }

    // GET /Labels/BoxLabel?boxId=...
    [HttpGet]
    public async Task<IActionResult> BoxLabel(Guid boxId)
    {
        using var db = await OpenAsync();

        var b = await db.QueryFirstOrDefaultAsync(@"
select id, box_no, label_code, notes
from ged.box
where tenant_id=@tid and id=@boxId;", new { tid = TenantId, boxId });

        if (b == null) return NotFound("Caixa não encontrada.");

        return View(b);
    }

    // GET /Labels/DocumentLabel?docId=...
    [HttpGet]
    public async Task<IActionResult> DocumentLabel(Guid docId)
    {
        using var db = await OpenAsync();

        var d = await db.QueryFirstOrDefaultAsync(@"
select id, code, title, status
from ged.document
where tenant_id=@tid and id=@docId;", new { tid = TenantId, docId });

        if (d == null) return NotFound("Documento não encontrado.");

        return View(d);
    }
}