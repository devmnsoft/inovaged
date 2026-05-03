using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;

namespace InovaGed.Web.Controllers;

[Authorize]
public class LabelsController : GedControllerBase
{
    public LabelsController(IDbConnectionFactory dbFactory) : base(dbFactory)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Boxes()
    {
        using var db = await OpenAsync();

        var rows = await db.QueryAsync("""
select
    b.id,
    b.box_no,
    b.label_code,
    b.notes,
    b.reg_date,
    b.reg_status,
    pl.location_code,
    pl.building,
    pl.room
from ged.box b
left join ged.physical_location pl
  on pl.tenant_id=b.tenant_id
 and pl.id=b.location_id
 and pl.reg_status='A'
where b.tenant_id=@tid
  and b.reg_status='A'
order by b.box_no;
""", new { tid = TenantId });

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Documents()
    {
        using var db = await OpenAsync();

        var rows = await db.QueryAsync("""
select
    d.id,
    d.code,
    d.title,
    d.status,
    d.created_at,
    bx.box_no,
    bx.label_code as box_label_code
from ged.document d
left join ged.batch_item bi
  on bi.tenant_id=d.tenant_id
 and bi.document_id=d.id
 and bi.reg_status='A'
left join ged.box bx
  on bx.tenant_id=d.tenant_id
 and bx.id=bi.box_id
 and bx.reg_status='A'
where d.tenant_id=@tid
order by d.created_at desc
limit 300;
""", new { tid = TenantId });

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> BoxLabel(Guid boxId)
    {
        using var db = await OpenAsync();

        var b = await db.QueryFirstOrDefaultAsync("""
select
    b.id,
    b.box_no,
    b.label_code,
    b.notes,
    pl.location_code,
    pl.building,
    pl.room,
    pl.aisle,
    pl.rack,
    pl.shelf,
    pl.pallet
from ged.box b
left join ged.physical_location pl
  on pl.tenant_id=b.tenant_id
 and pl.id=b.location_id
 and pl.reg_status='A'
where b.tenant_id=@tid
  and b.id=@boxId
  and b.reg_status='A';
""", new { tid = TenantId, boxId });

        if (b == null) return NotFound("Caixa não encontrada.");

        await RegisterLabelPrintAsync(db, "BOX", boxId, null);

        return View(b);
    }

    [HttpGet]
    public async Task<IActionResult> DocumentLabel(Guid docId)
    {
        using var db = await OpenAsync();

        var d = await db.QueryFirstOrDefaultAsync("""
select
    d.id,
    d.code,
    d.title,
    d.status,
    bx.box_no,
    bx.label_code as box_label_code
from ged.document d
left join ged.batch_item bi
  on bi.tenant_id=d.tenant_id
 and bi.document_id=d.id
 and bi.reg_status='A'
left join ged.box bx
  on bx.tenant_id=d.tenant_id
 and bx.id=bi.box_id
 and bx.reg_status='A'
where d.tenant_id=@tid
  and d.id=@docId;
""", new { tid = TenantId, docId });

        if (d == null) return NotFound("Documento não encontrado.");

        await RegisterLabelPrintAsync(db, "DOCUMENT", null, docId);

        return View(d);
    }

    private async Task RegisterLabelPrintAsync(
        System.Data.IDbConnection db,
        string labelType,
        Guid? boxId,
        Guid? documentId)
    {
        await db.ExecuteAsync("""
insert into ged.label_print
(
    id,
    tenant_id,
    box_id,
    document_id,
    label_type,
    printed_by,
    printed_at
)
values
(
    gen_random_uuid(),
    @tenant_id,
    @box_id,
    @document_id,
    @label_type,
    @printed_by,
    now()
);
""", new
        {
            tenant_id = TenantId,
            box_id = boxId,
            document_id = documentId,
            label_type = labelType,
            printed_by = UserId
        });
    }
}