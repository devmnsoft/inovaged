using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Security;
using InovaGed.Application.PhysicalArchive;
using InovaGed.Web.Models.Labels;
using InovaGed.Application.Labels.Printing;
using System.Text.Json;
using System.Data;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
public class LabelsController : GedControllerBase
{
    internal sealed record ClassificationPlanSchemaInfo(
        bool HasCode,
        bool HasTitle,
        bool HasDescription,
        bool HasFinalDestination,
        bool DocumentHasClassificationId,
        bool HasActivityType = false);

    private readonly ILabelPrintRegistrar _printRegistrar;
    private readonly ILabelTemplateService _templates;
    private readonly ILabelQrCodeService _qrCodes;
    private readonly ILabelPayloadBuilder _payloadBuilder;
    private readonly ILabelTemplateCatalogService _catalog;
    private readonly InovaGed.Application.Labels.ILabelTemplateManager _templateManager;
    private readonly ILabelPrintJobService _printJobs;
    private readonly ILabelPdfRenderService _pdf;

    public LabelsController(IDbConnectionFactory dbFactory, ILabelPrintRegistrar printRegistrar,
        ILabelTemplateService templates, ILabelQrCodeService qrCodes, ILabelPayloadBuilder payloadBuilder, ILabelTemplateCatalogService catalog, InovaGed.Application.Labels.ILabelTemplateManager templateManager,
        ILabelPrintJobService printJobs, ILabelPdfRenderService pdf) : base(dbFactory)
    {
        _printRegistrar = printRegistrar;
        _templates = templates;
        _qrCodes = qrCodes;
        _payloadBuilder = payloadBuilder;
        _catalog = catalog;
        _templateManager = templateManager;
        _printJobs=printJobs; _pdf=pdf;
    }

    [HttpGet]
    public async Task<IActionResult> PrintQueue([FromQuery] LabelPrintJobFilter filter,CancellationToken ct)
    { ViewBag.Filter=filter; return View(await _printJobs.ListAsync(TenantId,filter,ct)); }

    [HttpGet]
    public async Task<IActionResult> PrintJob(Guid id,CancellationToken ct)
    { var job=await _printJobs.GetAsync(TenantId,id,ct); return job is null?NotFound():View("PrintJobDetails",job); }

    [HttpGet]
    public async Task<IActionResult> PrintPreview(Guid id,CancellationToken ct)
    { var job=await _printJobs.GetAsync(TenantId,id,ct);if(job is null)return NotFound();if(UserId is not Guid uid)return Unauthorized();await _printJobs.MarkPreviewedAsync(TenantId,id,uid,ct);return View(job); }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePrintJob(CreatePrintJobInput input,CancellationToken ct)
    {
        if(UserId is not Guid uid)return Unauthorized();if(!ModelState.IsValid)return RedirectToAction(nameof(PrintWizard));
        var template=await _catalog.GetTemplateAsync(TenantId,input.TemplateCode,ct);
        if(!await _catalog.IsCompatibleAsync(TenantId,input.TemplateCode,input.SubjectType,ct))return BadRequest("Modelo incompatível.");
        using var db=await OpenAsync();dynamic? subject=input.SubjectType=="BOX"?await LoadBoxLabelAsync(db,input.SubjectId!.Value):await LoadDocumentLabelAsync(db,input.SubjectId!.Value);if(subject is null)return NotFound();
        var json=_payloadBuilder.Build(subject);var id=await _printJobs.CreateJobAsync(new(TenantId,uid,input.PrintMode,template.Code,template.Name,input.SubjectType,input.SubjectId,null,null,input.Copies,json,input.ReprintReason,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent),ct);
        return RedirectToAction(nameof(PrintPreview),new{id});
    }

    [HttpGet]
    public async Task<IActionResult> BatchPrint(CancellationToken ct,string subjectType="BOX")
    {
        subjectType=subjectType.ToUpperInvariant();using var db=await OpenAsync();
        ViewBag.SubjectType=subjectType;ViewBag.Templates=await _catalog.GetTemplatesAsync(TenantId,subjectType,null,ct);
        ViewBag.Rows=subjectType=="DOCUMENT"?await db.QueryAsync("select d.id,d.code control_number,d.title subject,b.box_no,coalesce(pl.location_code,'') location,coalesce(cp.title,'') classification,d.status,exists(select 1 from ged.label_print_history h where h.tenant_id=d.tenant_id and h.label_subject_id=d.id) already_printed from ged.document d left join ged.batch_item bi on bi.tenant_id=d.tenant_id and bi.document_id=d.id and bi.reg_status='A' left join ged.box b on b.tenant_id=d.tenant_id and b.id=bi.box_id left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id=d.classification_id where d.tenant_id=@tid order by d.created_at desc limit 300",new{tid=TenantId}):await db.QueryAsync("select b.id,coalesce(b.label_code,b.box_no::text) control_number,coalesce(b.notes,'Caixa física') subject,b.box_no,coalesce(pl.location_code,'') location,'' classification,b.reg_status status,exists(select 1 from ged.label_print_history h where h.tenant_id=b.tenant_id and h.label_subject_id=b.id) already_printed from ged.box b left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id where b.tenant_id=@tid and b.reg_status='A' order by b.box_no limit 300",new{tid=TenantId});
        return View(new CreateBatchPrintJobInput{SubjectType=subjectType});
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBatchPrintJob(CreateBatchPrintJobInput input,CancellationToken ct)
    {
        if(UserId is not Guid uid)return Unauthorized();if(input.SubjectIds.Count==0)return BadRequest("Selecione ao menos um item.");
        var template=await _catalog.GetTemplateAsync(TenantId,input.TemplateCode,ct);if(!await _catalog.IsCompatibleAsync(TenantId,input.TemplateCode,input.SubjectType,ct))return BadRequest("Modelo incompatível.");
        using var db=await OpenAsync();var items=new List<LabelPrintBatchItem>();var order=0;
        foreach(var sid in input.SubjectIds.Distinct()){dynamic? row=input.SubjectType=="BOX"?await LoadBoxLabelAsync(db,sid):await LoadDocumentLabelAsync(db,sid);if(row is null)return NotFound();items.Add(new(sid,input.SubjectType,null,null,_payloadBuilder.Build(row),order++));}
        var id=await _printJobs.CreateBatchJobAsync(new(TenantId,uid,input.PrintMode,template.Code,template.Name,input.SubjectType,input.Copies,items,input.ReprintReason,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent),ct);return RedirectToAction(nameof(PrintPreview),new{id});
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPrinted(Guid id,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await _printJobs.MarkPrintedAsync(TenantId,id,uid,ct);TempData["Success"]="Impressão registrada para auditoria.";return RedirectToAction(nameof(PrintJob),new{id});}
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPrintJob(Guid id,string reason,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await _printJobs.CancelAsync(TenantId,id,uid,reason,ct);return RedirectToAction(nameof(PrintQueue));}
    [HttpGet]
    public async Task<IActionResult> GeneratePdf(Guid id,CancellationToken ct){var result=await _pdf.GeneratePdfAsync(TenantId,id,ct);return File(result.Content,result.ContentType,result.FileName);}

    [HttpGet]
    public async Task<IActionResult> Calibration(CancellationToken ct,string templateCode="FACTORY_BOX_V1"){using var db=await OpenAsync();var model=await db.QuerySingleOrDefaultAsync<LabelCalibrationInput>(new CommandDefinition("select template_code TemplateCode,printer_name PrinterName,margin_top_mm MarginTopMm,margin_left_mm MarginLeftMm,scale_percent ScalePercent,label_width_mm LabelWidthMm,label_height_mm LabelHeightMm,gap_x_mm GapXMm,gap_y_mm GapYMm,labels_per_page LabelsPerPage from ged.label_print_calibration where tenant_id=@tid and template_code=@templateCode and reg_status='A' order by updated_at desc nulls last limit 1",new{tid=TenantId,templateCode},cancellationToken:ct));return View(model??new LabelCalibrationInput{TemplateCode=templateCode});}
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Calibration(LabelCalibrationInput input,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();if(!ModelState.IsValid)return View(input);using var db=await OpenAsync();await db.ExecuteAsync(new CommandDefinition("""insert into ged.label_print_calibration(tenant_id,template_code,printer_name,margin_top_mm,margin_left_mm,scale_percent,label_width_mm,label_height_mm,gap_x_mm,gap_y_mm,labels_per_page,created_by) values(@tid,@TemplateCode,nullif(@PrinterName,''),@MarginTopMm,@MarginLeftMm,@ScalePercent,@LabelWidthMm,@LabelHeightMm,@GapXMm,@GapYMm,@LabelsPerPage,@uid) on conflict(tenant_id,template_code,(coalesce(printer_name,'DEFAULT'))) where reg_status='A' do update set margin_top_mm=excluded.margin_top_mm,margin_left_mm=excluded.margin_left_mm,scale_percent=excluded.scale_percent,label_width_mm=excluded.label_width_mm,label_height_mm=excluded.label_height_mm,gap_x_mm=excluded.gap_x_mm,gap_y_mm=excluded.gap_y_mm,labels_per_page=excluded.labels_per_page,updated_by=@uid,updated_at=now()""",new{tid=TenantId,uid,input.TemplateCode,input.PrinterName,input.MarginTopMm,input.MarginLeftMm,input.ScalePercent,input.LabelWidthMm,input.LabelHeightMm,input.GapXMm,input.GapYMm,input.LabelsPerPage},cancellationToken:ct));TempData["Success"]="Calibração salva.";return RedirectToAction(nameof(Calibration),new{input.TemplateCode});}
    [HttpGet]
    public IActionResult TestSheet()=>View("PrintTestSheet",new LabelCalibrationInput());

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> PrintWizard(string? subjectType, Guid? subjectId, string? mode, string? templateCode, CancellationToken ct)
    {
        subjectType = subjectType?.ToUpperInvariant() ?? LabelSubjectType.Box;
        mode = mode?.ToUpperInvariant() ?? LabelPrintMode.Factory;
        var options = await _catalog.GetTemplatesAsync(TenantId,subjectType,mode,ct);
        if (_catalog.IsTemporaryCatalog) ViewBag.CatalogMigrationWarning = "As migrations de modelos de etiqueta ainda não foram aplicadas. O sistema está usando catálogo temporário.";
        if (string.IsNullOrWhiteSpace(templateCode) || !await _catalog.IsCompatibleAsync(TenantId,templateCode,subjectType,ct) || !options.Any(x=>x.Code==templateCode)) templateCode=options.FirstOrDefault()?.Code ?? "";
        ViewBag.Templates=options;
        return View(new LabelPrintWizardInputModel { SubjectType=subjectType, SubjectId=subjectId, PrintMode=mode, TemplateCode=templateCode });
    }

    [HttpGet]
    public async Task<IActionResult> Templates(string? subjectType, string? mode, CancellationToken ct)
        => Json(await _catalog.GetTemplatesAsync(TenantId,subjectType?.ToUpperInvariant() ?? LabelSubjectType.Box,mode?.ToUpperInvariant(),ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(LabelPrintWizardInputModel input, CancellationToken ct) => await ProcessWizard(input, false, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Print(LabelPrintWizardInputModel input, CancellationToken ct) => await ProcessWizard(input, true, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintBatch(LabelBatchPrintInputModel input, CancellationToken ct)
    {
        if (input.SubjectIds.Count==0) ModelState.AddModelError(nameof(input.SubjectIds),"Selecione ao menos uma etiqueta.");
        if (!await _catalog.IsCompatibleAsync(TenantId,input.TemplateCode,input.SubjectType,ct)) ModelState.AddModelError(nameof(input.TemplateCode),"O modelo selecionado não é compatível com o tipo de origem escolhido.");
        if (!ModelState.IsValid) return await PrintWizard(input.SubjectType,null,input.PrintMode,input.TemplateCode,ct);
        foreach(var id in input.SubjectIds) {
            var wizard=new LabelPrintWizardInputModel { SubjectType=input.SubjectType,SubjectId=id,PrintMode=input.PrintMode,TemplateCode=input.TemplateCode,Copies=input.Copies,ReprintReason=input.ReprintReason };
            var result=await ProcessWizard(wizard,true,ct); if(result is NotFoundResult) return result;
        }
        TempData["Success"]=$"{input.SubjectIds.Count} etiqueta(s) registradas no lote.";
        return RedirectToAction(nameof(History));
    }

    private async Task<IActionResult> ProcessWizard(LabelPrintWizardInputModel input, bool register, CancellationToken ct)
    {
        if (!LabelPrintMode.IsValid(input.PrintMode) || !await _catalog.IsCompatibleAsync(TenantId,input.TemplateCode,input.SubjectType,ct)) ModelState.AddModelError(nameof(input.TemplateCode),"O modelo selecionado não é compatível com o tipo de origem escolhido.");
        LabelTemplateOption? template=null; try { template=await _catalog.GetTemplateAsync(TenantId,input.TemplateCode,ct); } catch { ModelState.AddModelError(nameof(input.TemplateCode),"Modelo obrigatório."); }
        if (!ModelState.IsValid || template is null) { ViewBag.Templates=await _catalog.GetTemplatesAsync(TenantId,input.SubjectType,input.PrintMode,ct); return View("PrintWizard",input); }
        if (input.PrintMode==LabelPrintMode.Custom) {
            if(input.SubjectType==LabelSubjectType.Box && input.SubjectId is Guid box) return RedirectToAction(nameof(LocDeskBox),new {boxId=box});
            if(input.SubjectType==LabelSubjectType.Document && input.SubjectId is Guid doc) return RedirectToAction(nameof(LocDeskFolder),new {docId=doc});
            return View("LocDesk",input.CustomFields);
        }
        using var db=await OpenAsync(); object? subject=input.SubjectType==LabelSubjectType.Box ? await LoadBoxLabelAsync(db,input.SubjectId!.Value) : await LoadDocumentLabelAsync(db,input.SubjectId!.Value);
        if(subject is null) return NotFound();
        if(register) { if(UserId is not Guid uid) return Unauthorized(); var snapshot=new { printMode=input.PrintMode,templateCode=template.Code,templateName=template.Name,subjectType=input.SubjectType,subjectId=input.SubjectId,controlNumber=(string?)null,location=(string?)null,printedFields=subject };
            try { await _printRegistrar.RegisterAsync(new(TenantId,uid,input.SubjectType,input.SubjectId!.Value,template.Code,_payloadBuilder.Build(snapshot),HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),input.ReprintReason),ct); } catch(InvalidOperationException ex) { ModelState.AddModelError(nameof(input.ReprintReason),ex.Message); ViewBag.Templates=await _catalog.GetTemplatesAsync(TenantId,input.SubjectType,input.PrintMode,ct); return View("PrintWizard",input); } }
        ViewBag.QrSvg=_qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}/LabelTracking/Trace?payloadOrCode={input.SubjectId}"); ViewBag.PrintRegistered=register; ViewBag.Copies=input.Copies;
        return View(template.ViewName,subject);
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
 left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id=coalesce(dc.classification_id,d.classification_id)
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
        ViewBag.PrintRegistered = true;
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
        ViewBag.PrintRegistered = true;
        return View("DocumentLabel", snapshot);
    }

    private async Task RegisterAsync(string type, Guid subjectId, object snapshot, string? reason, CancellationToken ct)
    {
        if (UserId is not Guid userId) throw new UnauthorizedAccessException("Usuário autenticado obrigatório.");
        var template = _templates.GetCurrent(type);
        await _printRegistrar.RegisterAsync(new LabelPrintRequest(
            TenantId, userId, type, subjectId, $"{template.Code}_V{template.Version}", _payloadBuilder.Build(snapshot),
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
 left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id=coalesce(dc.classification_id,d.classification_id)
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
    public Task<IActionResult> LocDesk(CancellationToken ct)
        => Task.FromResult<IActionResult>(RedirectToAction(nameof(PrintWizard), new { mode=LabelPrintMode.Custom, subjectType=LabelSubjectType.Document, templateCode=LabelTemplateCode.LocDeskFolder }));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewLocDesk(LocDeskLabelInputModel input, CancellationToken ct)
    {
        NormalizeLocDesk(input);
        if (!ModelState.IsValid) return View("LocDesk", input);
        return await RenderLocDesk(input, false,ct);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintLocDesk(LocDeskLabelInputModel input, CancellationToken ct)
    {
        NormalizeLocDesk(input);
        if (!ModelState.IsValid) return View("LocDesk", input);
        try
        {
            var subjectId = await EnsureLocDeskSubjectAsync(input, ct);
            await RegisterLocDeskAsync(input, subjectId, input.ReprintReason, ct);
            return await RenderLocDesk(input,true,ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(input.ReprintReason), ex.Message);
            return View("LocDesk", input);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintLocDeskBatch(LocDeskLabelBatchPrintModel input, CancellationToken ct)
    {
        if (input.Labels.Count == 0) { ModelState.AddModelError("", "Selecione ao menos uma etiqueta."); return View("LocDesk", new LocDeskLabelInputModel()); }
        foreach (var label in input.Labels)
        {
            NormalizeLocDesk(label);
            label.ReprintReason ??= input.ReprintReason;
            if (!TryValidateModel(label)) return View("LocDesk", label);
            var subjectId = await EnsureLocDeskSubjectAsync(label, ct);
            await RegisterLocDeskAsync(label, subjectId, label.ReprintReason, ct);
        }
        var first = input.Labels[0];
        var qr = CreateLocDeskQr(first, first.BoxId ?? first.DocumentId);
        return View(first.LabelKind == LocDeskLabelKind.Box ? "LocDeskBoxLabel" : "LocDeskFolderLabel",
            new LocDeskLabelRenderModel { Label = first, Labels = input.Labels, QrSvg = qr, PrintRegistered = true, Template=await LoadLocDeskTemplate(first,ct) });
    }

    [HttpGet]
    public async Task<IActionResult> LocDeskBox(Guid boxId, CancellationToken ct) => await LocDeskBoxFromPhysical(boxId, ct);

    [HttpGet]
    public async Task<IActionResult> LocDeskBoxFromPhysical(Guid boxId, CancellationToken ct)
    {
        using var db = await OpenAsync();
        var schema = await GetClassificationPlanSchemaInfoAsync(db, ct);
        var classificationCodeExpr = BuildClassificationCodeExpression(schema);
        var classificationTitleExpr = BuildClassificationTitleExpression(schema);
        var finalDestinationExpr = BuildFinalDestinationExpression(schema);
        var documentClassificationJoinExpr = BuildDocumentClassificationJoinExpression(schema);
        var sql = $"""
select 'BOX' as LabelKind, b.id as BoxId, coalesce(nullif(b.label_code,''),lpad(b.box_no::text,4,'0')) as ControlNumber,
 coalesce(pl.location_code, concat_ws('.',pl.building,pl.room,pl.aisle,pl.rack,pl.shelf,pl.pallet),'') as Location,
 case when count(distinct d.id)=0 then coalesce(b.notes,'Caixa física') when count(distinct d.title)=1 then max(d.title) else 'Conteúdo misto - revisar classificação' end as Subject,
 case when count(distinct cp.id)=1 then concat_ws(' - ',{classificationCodeExpr},{classificationTitleExpr}) when count(distinct cp.id)>1 then 'Conteúdo misto' else '' end as Classification,
 case when count(distinct d.id)=0 or count(d.created_at)=0 then '' when min(d.created_at)::date=max(d.created_at)::date then to_char(min(d.created_at),'YYYY') else coalesce(concat(to_char(min(d.created_at),'YYYY'),' a ',to_char(max(d.created_at),'YYYY')),'') end as DocumentPeriod,
 {finalDestinationExpr} as CurrentPhase
from ged.box b left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id and pl.reg_status='A'
left join ged.batch_item bi on bi.tenant_id=b.tenant_id and bi.box_id=b.id and bi.reg_status='A'
left join ged.document d on d.tenant_id=bi.tenant_id and d.id=bi.document_id
left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id={documentClassificationJoinExpr}
where b.tenant_id=@tid and b.id=@boxId and b.reg_status='A'
group by b.id,b.label_code,b.box_no,b.notes,pl.location_code,pl.building,pl.room,pl.aisle,pl.rack,pl.shelf,pl.pallet
""";
        var model = await db.QueryFirstOrDefaultAsync<LocDeskLabelInputModel>(new CommandDefinition(sql, new { tid=TenantId, boxId }, cancellationToken:ct));
        if (model is null) return NotFound("Caixa não encontrada.");
        ApplyDefaults(model);
        return View("LocDesk", model);
    }

    [HttpGet]
    public async Task<IActionResult> LocDeskFolder(Guid docId, CancellationToken ct) => await LocDeskFolderFromDocument(docId, ct);

    [HttpGet]
    public async Task<IActionResult> LocDeskFolderFromDocument(Guid docId, CancellationToken ct)
    {
        using var db = await OpenAsync();
        var schema = await GetClassificationPlanSchemaInfoAsync(db, ct);
        var classificationCodeExpr = BuildClassificationCodeExpression(schema, aggregate: false);
        var classificationTitleExpr = BuildClassificationTitleExpression(schema, aggregate: false);
        var finalDestinationExpr = BuildFinalDestinationExpression(schema, aggregate: false);
        var documentClassificationJoinExpr = BuildDocumentClassificationJoinExpression(schema);
        var activityExpr = schema.HasActivityType ? "coalesce(cp.activity_type,'FIM')" : "'FIM'";
        var sql = $"""
select 'FOLDER' as LabelKind,d.id as DocumentId,b.id as BoxId,coalesce(nullif(d.code,''),left(d.id::text,8)) as ControlNumber,
coalesce(d.title,'') as Subject,concat_ws(' - ',{classificationCodeExpr},{classificationTitleExpr}) as Classification,
coalesce(pl.location_code,concat_ws('.',pl.building,pl.room,pl.aisle,pl.rack,pl.shelf,pl.pallet),'') as Location,
to_char(d.created_at,'YYYY') as DocumentPeriod,{activityExpr} as Activity,{finalDestinationExpr} as CurrentPhase
from ged.document d left join ged.batch_item bi on bi.tenant_id=d.tenant_id and bi.document_id=d.id and bi.reg_status='A'
left join ged.box b on b.tenant_id=d.tenant_id and b.id=bi.box_id and b.reg_status='A'
left join ged.physical_location pl on pl.tenant_id=b.tenant_id and pl.id=b.location_id and pl.reg_status='A'
left join ged.document_classification dc on dc.tenant_id=d.tenant_id and dc.document_id=d.id and dc.reg_status='A'
left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id={documentClassificationJoinExpr}
where d.tenant_id=@tid and d.id=@docId limit 1
""";
        var model = await db.QueryFirstOrDefaultAsync<LocDeskLabelInputModel>(new CommandDefinition(sql, new { tid=TenantId, docId }, cancellationToken:ct));
        if (model is null) return NotFound("Documento não encontrado.");
        ApplyDefaults(model);
        return View("LocDesk", model);
    }

    private async Task<ClassificationPlanSchemaInfo> GetClassificationPlanSchemaInfoAsync(IDbConnection db, CancellationToken ct)
    {
        const string sql = """
select
    exists (select 1 from information_schema.columns where table_schema='ged' and table_name='classification_plan' and column_name='code') as "HasCode",
    exists (select 1 from information_schema.columns where table_schema='ged' and table_name='classification_plan' and column_name='title') as "HasTitle",
    exists (select 1 from information_schema.columns where table_schema='ged' and table_name='classification_plan' and column_name='description') as "HasDescription",
    exists (select 1 from information_schema.columns where table_schema='ged' and table_name='classification_plan' and column_name='final_destination') as "HasFinalDestination",
    exists (select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='classification_id') as "DocumentHasClassificationId",
    exists (select 1 from information_schema.columns where table_schema='ged' and table_name='classification_plan' and column_name='activity_type') as "HasActivityType"
""";
        return await db.QuerySingleAsync<ClassificationPlanSchemaInfo>(new CommandDefinition(sql, cancellationToken: ct));
    }

    internal static string BuildClassificationCodeExpression(ClassificationPlanSchemaInfo schema, bool aggregate = true)
        => schema.HasCode ? $"nullif({(aggregate ? "max(cp.code)" : "cp.code")}, '')" : "null";

    internal static string BuildClassificationTitleExpression(ClassificationPlanSchemaInfo schema, bool aggregate = true)
    {
        var parts = new List<string>();
        if (schema.HasTitle) parts.Add($"nullif({(aggregate ? "max(cp.title)" : "cp.title")}, '')");
        if (schema.HasDescription) parts.Add($"nullif({(aggregate ? "max(cp.description)" : "cp.description")}, '')");
        if (schema.HasCode) parts.Add($"nullif({(aggregate ? "max(cp.code)" : "cp.code")}, '')");
        return parts.Count == 0 ? "null" : $"coalesce({string.Join(", ", parts)})";
    }

    internal static string BuildFinalDestinationExpression(ClassificationPlanSchemaInfo schema, bool aggregate = true)
        => schema.HasFinalDestination ? $"coalesce({(aggregate ? "max(cp.final_destination)" : "cp.final_destination")}, '')" : "''";

    internal static string BuildDocumentClassificationJoinExpression(ClassificationPlanSchemaInfo schema)
        => schema.DocumentHasClassificationId ? "coalesce(dc.classification_id, d.classification_id)" : "dc.classification_id";

    private async Task<IActionResult> RenderLocDesk(LocDeskLabelInputModel input,bool registered,CancellationToken ct)
    {
        var qr = CreateLocDeskQr(input, input.BoxId ?? input.DocumentId);
        var model = new LocDeskLabelRenderModel { Label=input,QrSvg=qr,PrintRegistered=registered,Template=await LoadLocDeskTemplate(input,ct) };
        return View(input.LabelKind == LocDeskLabelKind.Box ? "LocDeskBoxLabel" : "LocDeskFolderLabel", model);
    }

    private async Task<InovaGed.Application.Labels.LabelTemplateDetails?> LoadLocDeskTemplate(LocDeskLabelInputModel input,CancellationToken ct)
    {
        var code=input.LabelKind==LocDeskLabelKind.Box?LabelTemplateCode.LocDeskBox:LabelTemplateCode.LocDeskFolder;
        try { var option=await _catalog.GetTemplateAsync(TenantId,code,ct); return option.Id is Guid id?await _templateManager.GetAsync(TenantId,id,ct):null; } catch(KeyNotFoundException){return null;}
    }

    private string CreateLocDeskQr(LocDeskLabelInputModel input, Guid? id)
    {
        var path = input.BoxId is Guid box ? $"/Physical/BoxContents?boxId={box}" : input.DocumentId is Guid doc ? $"/Ged/Document/{doc}" : $"/Labels/Trace/{id}";
        return _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}{path}");
    }

    private async Task<Guid> EnsureLocDeskSubjectAsync(LocDeskLabelInputModel input, CancellationToken ct)
    {
        if (input.LabelKind == LocDeskLabelKind.Box && input.BoxId is Guid box) return box;
        if (input.LabelKind == LocDeskLabelKind.Folder && input.DocumentId is Guid doc) return doc;
        if (UserId is not Guid userId) throw new UnauthorizedAccessException();
        using var db = await OpenAsync();
        var id = Guid.NewGuid();
        var payload = _payloadBuilder.Build(input);
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.locdesk_label_draft(id,tenant_id,label_kind,archive_title,process_number,control_number,volume_number,volume_total,subject,details,activity,classification,support,document_period,current_phase,elimination_forecast,elimination_status,led_number,location,source_box_id,source_document_id,qr_payload,created_by)
values(@id,@tid,@LabelKind,@ArchiveTitle,@ProcessNumber,@ControlNumber,@VolumeNumber,@VolumeTotal,@Subject,@Details,@Activity,@Classification,@Support,@DocumentPeriod,@CurrentPhase,@EliminationForecast,@EliminationStatus,@LedNumber,@Location,@BoxId,@DocumentId,cast(@payload as text),@userId)
""", new { id,tid=TenantId,userId,payload,input.LabelKind,input.ArchiveTitle,input.ProcessNumber,input.ControlNumber,input.VolumeNumber,input.VolumeTotal,input.Subject,input.Details,input.Activity,input.Classification,input.Support,input.DocumentPeriod,input.CurrentPhase,input.EliminationForecast,input.EliminationStatus,input.LedNumber,input.Location,input.BoxId,input.DocumentId }, cancellationToken:ct));
        return id;
    }

    private async Task RegisterLocDeskAsync(LocDeskLabelInputModel input, Guid subjectId, string? reason, CancellationToken ct)
    {
        if (UserId is not Guid userId) throw new UnauthorizedAccessException("Usuário autenticado obrigatório.");
        var type = input.BoxId.HasValue ? "BOX" : input.DocumentId.HasValue ? "DOCUMENT" : "MANUAL_LABEL";
        var template = input.LabelKind == LocDeskLabelKind.Box ? "LOCDESK_CAIXA_V1" : "LOCDESK_PASTA_V1";
        var option=await _catalog.GetTemplateAsync(TenantId,template,ct);
        var details=option.Id is Guid templateId?await _templateManager.GetAsync(TenantId,templateId,ct):null;
        var snapshot=new { printMode=option.Mode,templateCode=option.Code,templateName=option.Name,templateVersion=details?.Template.Version??1,isDefault=option.IsDefault,configuration=details,input };
        await _printRegistrar.RegisterAsync(new LabelPrintRequest(TenantId,userId,type,subjectId,template,_payloadBuilder.Build(snapshot),HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),reason),ct);
    }

    private static void NormalizeLocDesk(LocDeskLabelInputModel input)
    {
        input.LabelKind = input.LabelKind?.Trim().ToUpperInvariant() ?? LocDeskLabelKind.Folder;
        input.ControlNumber = input.ControlNumber?.Trim() ?? "";
        if (long.TryParse(input.ControlNumber, out var value)) input.ControlNumber = value.ToString("D4");
        input.ArchiveTitle = input.ArchiveTitle?.Trim() ?? "";
    }
    private static void ApplyDefaults(LocDeskLabelInputModel model)
    {
        model.ArchiveTitle = "ARQUIVO LOCDESCK ANANINDEUA"; model.VolumeNumber=1; model.VolumeTotal=1;
        model.Details ??="0"; model.Activity=string.IsNullOrWhiteSpace(model.Activity)?"FIM":model.Activity; model.Support="1. Papel"; model.LedNumber="N/A"; model.Copies=1;
        NormalizeLocDesk(model);
    }

    [HttpGet("/Labels/Trace/{id:guid}")]
    public async Task<IActionResult> Trace(Guid id, CancellationToken ct)
    {
        using var db=await OpenAsync();
        var draft=await db.QueryFirstOrDefaultAsync(new CommandDefinition("select * from ged.locdesk_label_draft where tenant_id=@tid and id=@id and reg_status='A'",new {tid=TenantId,id},cancellationToken:ct));
        return draft is null ? NotFound() : View(draft);
    }

    [HttpGet]
    public async Task<IActionResult> History(string? q, string? mode, string? template, string? type)
    {
        using var db = await OpenAsync();

        q = (q ?? "").Trim();
        ViewBag.Q = q;
        mode=(mode??"").Trim().ToUpperInvariant(); template=(template??"").Trim().ToUpperInvariant(); type=(type??"").Trim().ToUpperInvariant();

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
    coalesce(lp.snapshot_json->>'printMode',case when lp.template_code like 'LOCDESK%' then 'CUSTOM' else 'FACTORY' end) as print_mode,
    coalesce(lp.snapshot_json->>'templateName',case lp.template_code when 'FACTORY_BOX_V1' then 'Padrão do Sistema - Caixa' when 'FACTORY_DOCUMENT_V1' then 'Padrão do Sistema - Documento/Pasta' when 'LOCDESK_CAIXA_V1' then 'LocDesk - Caixa' when 'LOCDESK_PASTA_V1' then 'LocDesk - Pasta' else lp.template_code end) as template_name,
    lp.snapshot_sha256,
    lp.reprint_reason,
    lp.snapshot_json->>'controlNumber' as control_number,
    lp.snapshot_json->>'location' as location,
    lp.snapshot_json->>'subject' as locdesk_subject,
    lp.snapshot_json->>'classification' as locdesk_classification
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
  and (@type='' or lp.label_subject_type=@type)
  and (@template='' or lp.template_code=@template)
  and (@mode='' or @mode=coalesce(lp.snapshot_json->>'printMode',case when lp.template_code like 'LOCDESK%' then 'CUSTOM' else 'FACTORY' end))
  and (
    @q = ''
    or coalesce(lp.label_subject_type,'') ilike ('%'||@q||'%')
    or coalesce(b.label_code,'') ilike ('%'||@q||'%')
    or coalesce(b.box_no::text,'') ilike ('%'||@q||'%')
    or coalesce(d.code,'') ilike ('%'||@q||'%')
    or coalesce(d.title,'') ilike ('%'||@q||'%')
    or coalesce(u.name,'') ilike ('%'||@q||'%')
    or coalesce(lp.template_code,'') ilike ('%'||@q||'%')
    or coalesce(lp.snapshot_json->>'controlNumber','') ilike ('%'||@q||'%')
    or coalesce(lp.snapshot_json->>'location','') ilike ('%'||@q||'%')
    or coalesce(lp.snapshot_json->>'subject','') ilike ('%'||@q||'%')
    or coalesce(lp.snapshot_json->>'classification','') ilike ('%'||@q||'%')
  )
order by lp.printed_at desc
limit 500;", new { tid = TenantId, q, mode, template, type });

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> PrintDetails(Guid id,CancellationToken ct)
    {
        using var db=await OpenAsync();
        var row=await db.QuerySingleOrDefaultAsync(new CommandDefinition("""
select lp.id,lp.printed_at,lp.ip_address,lp.user_agent,lp.template_code,lp.snapshot_sha256,lp.reprint_reason,lp.snapshot_json::text snapshot_json,lp.label_subject_type,lp.label_subject_id,u.name printed_by_name,coalesce(lp.snapshot_json->>'templateName',t.name,lp.template_code) template_name,coalesce((lp.snapshot_json->>'templateVersion')::int,t.version,1) template_version,coalesce(lp.snapshot_json->>'printMode',t.print_mode) print_mode,coalesce((lp.snapshot_json->>'isDefault')::boolean,t.is_default,false) was_default
from ged.label_print_history lp left join ged.app_user u on u.tenant_id=lp.tenant_id and u.id=lp.printed_by left join ged.label_template t on t.code=lp.template_code and (t.tenant_id=lp.tenant_id or t.tenant_id is null) where lp.tenant_id=@tenantId and lp.id=@id
""",new{tenantId=TenantId,id},cancellationToken:ct));
        return row is null?NotFound():View(row);
    }
}
