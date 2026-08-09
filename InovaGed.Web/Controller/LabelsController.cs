using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Security;
using InovaGed.Application.PhysicalArchive;
using System.Text.Json;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
public class LabelsController : GedControllerBase
{
    private readonly ILabelPrintRegistrar _printRegistrar;
    private readonly ILabelTemplateService _templates;
    private readonly ILabelQrCodeService _qrCodes;

    public LabelsController(IDbConnectionFactory dbFactory, ILabelPrintRegistrar printRegistrar,
        ILabelTemplateService templates, ILabelQrCodeService qrCodes) : base(dbFactory)
    {
        _printRegistrar = printRegistrar;
        _templates = templates;
        _qrCodes = qrCodes;
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
    pl.pallet,
    mix.document_count,
    mix.classification_count,
    mix.retention_count,
    mix.confidentiality_count,
    mix.destination_count,
    mix.period_start,
    mix.period_end,
    (coalesce(mix.classification_count,0)>1 or coalesce(mix.retention_count,0)>1 or
     coalesce(mix.confidentiality_count,0)>1 or coalesce(mix.destination_count,0)>1 or
     (mix.period_end::date-mix.period_start::date)>3650) as mixed_content
from ged.box b
left join ged.physical_location pl
  on pl.tenant_id=b.tenant_id
 and pl.id=b.location_id
 and pl.reg_status='A'
left join lateral (
 select count(distinct d.id) document_count, count(distinct cp.id) classification_count,
        count(distinct cp.current_retention_text) retention_count,
        count(distinct cp.confidentiality_level) confidentiality_count,
        count(distinct cp.final_destination) destination_count,
        min(d.created_at) period_start, max(d.created_at) period_end
 from ged.batch_item bi join ged.document d on d.tenant_id=bi.tenant_id and d.id=bi.document_id
 left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
 left join ged.classification_plan cp on cp.tenant_id=dc.tenant_id and cp.id=dc.classification_plan_id
 where bi.tenant_id=b.tenant_id and bi.box_id=b.id and bi.reg_status='A'
) mix on true
where b.tenant_id=@tid
  and b.id=@boxId
  and b.reg_status='A';
""", new { tid = TenantId, boxId });

        if (b == null) return NotFound("Caixa não encontrada.");

        ViewBag.QrSvg = _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}/Physical/BoxContents?boxId={boxId}");
        return View(b);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintBoxLabel(Guid boxId, string? reprintReason, CancellationToken ct)
    {
        using var db = await OpenAsync();
        var snapshot = await LoadBoxLabelAsync(db, boxId);
        if (snapshot is null) return NotFound();
        await RegisterAsync("BOX", boxId, snapshot, reprintReason, ct);
        ViewBag.QrSvg = _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}/Physical/BoxContents?boxId={boxId}");
        ViewBag.AutoPrint = true;
        return View("BoxLabel", snapshot);
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

        ViewBag.QrSvg = _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}/Ged/Document/{docId}");
        return View(d);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintDocumentLabel(Guid docId, string? reprintReason, CancellationToken ct)
    {
        using var db = await OpenAsync();
        var snapshot = await LoadDocumentLabelAsync(db, docId);
        if (snapshot is null) return NotFound();
        await RegisterAsync("DOCUMENT", docId, snapshot, reprintReason, ct);
        ViewBag.QrSvg = _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}/Ged/Document/{docId}");
        ViewBag.AutoPrint = true;
        return View("DocumentLabel", snapshot);
    }

    private async Task RegisterAsync(string type, Guid subjectId, object snapshot, string? reason, CancellationToken ct)
    {
        if (UserId is not Guid userId) throw new UnauthorizedAccessException("Usuário autenticado obrigatório.");
        var template = _templates.GetCurrent(type);
        await _printRegistrar.RegisterAsync(new LabelPrintRequest(
            TenantId, userId, type, subjectId, $"{template.Code}_V{template.Version}", JsonSerializer.Serialize(snapshot),
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), reason), ct);
    }

    private async Task<dynamic?> LoadBoxLabelAsync(System.Data.IDbConnection db, Guid boxId)
        => await db.QueryFirstOrDefaultAsync("""
select b.id, b.box_no, b.label_code, b.notes, pl.location_code, pl.building, pl.room,
       pl.aisle, pl.rack, pl.shelf, pl.pallet, mix.*,
       (coalesce(mix.classification_count,0)>1 or coalesce(mix.retention_count,0)>1 or
        coalesce(mix.confidentiality_count,0)>1 or coalesce(mix.destination_count,0)>1 or
        (mix.period_end::date-mix.period_start::date)>3650) as mixed_content
from ged.box b left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id and pl.reg_status='A'
left join lateral (
 select count(distinct d.id) document_count, count(distinct cp.id) classification_count,
 count(distinct cp.current_retention_text) retention_count, count(distinct cp.confidentiality_level) confidentiality_count,
 count(distinct cp.final_destination) destination_count, min(d.created_at) period_start, max(d.created_at) period_end
 from ged.batch_item bi join ged.document d on d.tenant_id=bi.tenant_id and d.id=bi.document_id
 left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
 left join ged.classification_plan cp on cp.tenant_id=dc.tenant_id and cp.id=dc.classification_plan_id
 where bi.tenant_id=b.tenant_id and bi.box_id=b.id and bi.reg_status='A') mix on true
where b.tenant_id=@tid and b.id=@boxId and b.reg_status='A'
""", new { tid = TenantId, boxId });

    private async Task<dynamic?> LoadDocumentLabelAsync(System.Data.IDbConnection db, Guid docId)
        => await db.QueryFirstOrDefaultAsync("""
select d.id, d.code, d.title, d.status, bx.box_no, bx.label_code as box_label_code
from ged.document d left join ged.batch_item bi on bi.tenant_id=d.tenant_id and bi.document_id=d.id and bi.reg_status='A'
left join ged.box bx on bx.tenant_id=d.tenant_id and bx.id=bi.box_id and bx.reg_status='A'
where d.tenant_id=@tid and d.id=@docId
""", new { tid = TenantId, docId });

    [HttpGet]
    public async Task<IActionResult> History(string? q)
    {
        using var db = await OpenAsync();

        q = (q ?? "").Trim();
        ViewBag.Q = q;

        var rows = await db.QueryAsync(@"
select
    lp.id,
    lp.label_subject_type as label_type,
    lp.printed_at,
    u.name as printed_by_name,
    b.box_no,
    b.label_code,
    d.code as document_code,
    d.title as document_title,
    lp.ip_address,
    lp.user_agent,
    lp.template_code,
    lp.snapshot_sha256,
    lp.reprint_reason
from ged.label_print_history lp
left join ged.app_user u
  on u.tenant_id=lp.tenant_id
 and u.id=lp.printed_by
left join ged.box b
  on b.tenant_id=lp.tenant_id
 and b.id=lp.label_subject_id and lp.label_subject_type='BOX'
left join ged.document d
  on d.tenant_id=lp.tenant_id
 and d.id=lp.label_subject_id and lp.label_subject_type='DOCUMENT'
where lp.tenant_id=@tid
  and (
    @q = ''
    or coalesce(lp.label_subject_type,'') ilike ('%'||@q||'%')
    or coalesce(b.label_code,'') ilike ('%'||@q||'%')
    or coalesce(b.box_no::text,'') ilike ('%'||@q||'%')
    or coalesce(d.code,'') ilike ('%'||@q||'%')
    or coalesce(d.title,'') ilike ('%'||@q||'%')
    or coalesce(u.name,'') ilike ('%'||@q||'%')
  )
order by lp.printed_at desc
limit 500;", new { tid = TenantId, q });

        return View(rows);
    }
}
