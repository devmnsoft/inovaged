using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Security;
using InovaGed.Application.PhysicalArchive;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Models;
using InovaGed.Application.Labels.Printing;
using System.Text.Json;
using System.Data;
using System.Text;
using InovaGed.Web.Services;
using InovaGed.Web.Models.Branding;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
public class LabelsController : GedControllerBase
{
    private static readonly IReadOnlyList<LabelStudioTemplate> StudioTemplates = new List<LabelStudioTemplate>
    {
        Studio("FACTORY_BOX_V1", "Padrão do Sistema - Caixa", "Identificação institucional de caixas físicas.", "Caixa", "Fábrica", "BoxLabel", "1.0", "100 × 70 mm", true, false, "label-template-thumb-factory-box"),
        Studio("FACTORY_DOCUMENT_V1", "Padrão do Sistema - Documento/Pasta", "Identificação institucional de documentos e pastas.", "Documento", "Fábrica", "DocumentLabel", "1.0", "100 × 70 mm", true, false, "label-template-thumb-factory-document"),
        Studio("LOCDESK_CAIXA_V1", "LocDesk - Caixa", "Modelo arquivístico LocDesk para caixas.", "Caixa", "Personalizado", "LocDeskBoxLabel", "1.0", "174 × 110 mm", true, true, "label-template-thumb-locdesk-box"),
        Studio("LOCDESK_PASTA_V1", "LocDesk - Pasta", "Modelo arquivístico LocDesk para pastas, com QR Code.", "Pasta", "Personalizado", "LocDeskFolderLabel", "1.0", "174 × 110 mm", true, true, "label-template-thumb-locdesk-folder"),
        Studio("LOCDESK_PASTA_HOL_V1", "LocDesk - Pasta HOL", "Modelo oficial do Hospital Ophir Loyola.", "Pasta", "Personalizado", "LocDeskFolderHolLabel", "1.0", "174 × 110 mm", true, true, "label-template-thumb-locdesk-hol")
    };

    private static LabelStudioTemplate Studio(string code,string name,string description,string type,string mode,string view,string version,string dimensions,bool batch,bool manual,string thumb) =>
        new(code,name,description,type,mode,view,version,dimensions,batch,manual,true,code==LabelTemplateCode.FactoryBox,thumb,
            new[]{"Nº de Controle","Volume","Assunto","Detalhamento","Atividade","Classificação","Suporte","Período do Documento","Fase Atual","Previsão Eliminação","Situação Eliminação","Nº LED","LOCALIZAÇÃO"});
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
    private readonly ILabelPrintLogoResolver _logoResolver;
    private readonly ILogger<LabelsController> _logger;

    public LabelsController(IDbConnectionFactory dbFactory, ILabelPrintRegistrar printRegistrar,
        ILabelTemplateService templates, ILabelQrCodeService qrCodes, ILabelPayloadBuilder payloadBuilder, ILabelTemplateCatalogService catalog, InovaGed.Application.Labels.ILabelTemplateManager templateManager,
        ILabelPrintJobService printJobs, ILabelPdfRenderService pdf, ILabelPrintLogoResolver logoResolver,
        ILogger<LabelsController> logger) : base(dbFactory)
    {
        _printRegistrar = printRegistrar;
        _templates = templates;
        _qrCodes = qrCodes;
        _payloadBuilder = payloadBuilder;
        _catalog = catalog;
        _templateManager = templateManager;
        _printJobs=printJobs; _pdf=pdf; _logoResolver=logoResolver; _logger=logger;
    }

    [HttpGet("/Labels/Templates")]
    public IActionResult Templates() => View(StudioTemplates);

    [HttpGet("/Labels/Templates/{code}")]
    public IActionResult TemplateDetails(string code)
    {
        var template = FindStudioTemplate(code);
        return template is null ? NotFound() : View(template);
    }

    [HttpGet("/Labels/Templates/{code}/Preview")]
    public IActionResult TemplatePreview(string code)
    {
        var template = FindStudioTemplate(code);
        return template is null ? NotFound() : View("TemplatePreview", new LabelStudioPreviewViewModel(template, CreateStudioSample(code), false));
    }

    [HttpGet("/Labels/Templates/{code}/PrintSample")]
    public IActionResult TemplatePrintSample(string code)
    {
        var template = FindStudioTemplate(code);
        return template is null ? NotFound() : View("TemplatePreview", new LabelStudioPreviewViewModel(template, CreateStudioSample(code), true));
    }

    [HttpPost("/Labels/Templates/{code}/SetDefault"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStudioDefault(string code, CancellationToken ct)
    {
        if (FindStudioTemplate(code) is null) return NotFound();
        using var db = await OpenAsync();
        var id = await db.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("select id from ged.label_template where code=@code and (tenant_id=@tenantId or tenant_id is null) and reg_status='A' order by tenant_id nulls last limit 1", new { code, tenantId=TenantId }, cancellationToken:ct));
        if (id is null) return NotFound();
        await _templateManager.SetDefaultAsync(TenantId, id.Value, ct);
        TempData["Success"] = "Modelo definido como padrão.";
        return RedirectToAction(nameof(TemplateDetails), new { code });
    }

    private static LabelStudioTemplate? FindStudioTemplate(string code) => StudioTemplates.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    private static LocDeskLabelInputModel CreateStudioSample(string code)
    {
        var sample = CreateLocDeskPreviewDefaults();
        sample.TemplateCode = code;
        if (code != LabelTemplateCode.LocDeskFolderHol)
        {
            sample.ArchiveTitle = "ARQUIVO LOCDESCK ANANINDEUA"; sample.ControlNumber = "0001"; sample.VolumeNumber = 1; sample.VolumeTotal = 3;
            sample.Subject = "Fiscalização PJ - DPF's e documentos avulsos ref. RFF-F"; sample.Details = "Documentos avulsos"; sample.Classification = "321.2 - PESSOAS JURÍDICAS";
            sample.Support = "1. Papel"; sample.DocumentPeriod = "até 2004"; sample.CurrentPhase = "4. Eliminação"; sample.EliminationForecast = "2025";
            sample.EliminationStatus = "2. LED pendente de elaboração"; sample.Location = "LOC.AN.101.E1.P1";
        }
        return sample;
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

    [HttpPost("/Labels/Batch/Preview"),ValidateAntiForgeryToken]
    public Task<IActionResult> BatchPreview(CreateBatchPrintJobInput input,CancellationToken ct)=>CreateBatchPrintJob(input,ct);

    [HttpPost("/Labels/Batch/Print"),ValidateAntiForgeryToken]
    public Task<IActionResult> BatchPrintSubmit(CreateBatchPrintJobInput input,CancellationToken ct)=>CreateBatchPrintJob(input,ct);

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPrinted(Guid id,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await _printJobs.MarkPrintedAsync(TenantId,id,uid,ct);TempData["Success"]="Impressão registrada para auditoria.";return RedirectToAction(nameof(PrintJob),new{id});}
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPrintJob(Guid id,string reason,CancellationToken ct){if(UserId is not Guid uid)return Unauthorized();await _printJobs.CancelAsync(TenantId,id,uid,reason,ct);return RedirectToAction(nameof(PrintQueue));}
    [HttpGet]
    public async Task<IActionResult> GeneratePdf(Guid id,CancellationToken ct){var result=await _pdf.GeneratePdfAsync(TenantId,id,ct);return File(result.Content,result.ContentType,result.FileName);}

    [HttpGet("/Labels/Calibration")]
    public async Task<IActionResult> Calibration(CancellationToken ct) => View(await LoadCalibrationPageAsync(null, ct));

    [HttpGet("/Labels/Calibration/Create")]
    public async Task<IActionResult> CalibrationCreate(CancellationToken ct) => View("Calibration", await LoadCalibrationPageAsync(new LabelPrintProfileInput(), ct));

    [HttpPost("/Labels/Calibration/Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CalibrationCreate(LabelPrintProfileInput input, CancellationToken ct)
    {
        if (UserId is not Guid uid) return Unauthorized();
        if (!ModelState.IsValid) return View("Calibration", await LoadCalibrationPageAsync(input, ct));
        using var db = await OpenAsync();
        await db.ExecuteAsync(new CommandDefinition("""insert into ged.label_print_profile(tenant_id,profile_name,printer_name,paper_size,orientation,margin_top_mm,margin_left_mm,offset_x_mm,offset_y_mm,scale_percent,label_gap_x_mm,label_gap_y_mm,is_default,notes,created_by) values(@tid,@ProfileName,nullif(@PrinterName,''),@PaperSize,@Orientation,@MarginTopMm,@MarginLeftMm,@OffsetXMm,@OffsetYMm,@ScalePercent,@LabelGapXMm,@LabelGapYMm,@IsDefault,nullif(@Notes,''),@uid)""", new { tid=TenantId,uid,input.ProfileName,input.PrinterName,input.PaperSize,input.Orientation,input.MarginTopMm,input.MarginLeftMm,input.OffsetXMm,input.OffsetYMm,input.ScalePercent,input.LabelGapXMm,input.LabelGapYMm,input.IsDefault,input.Notes }, cancellationToken:ct));
        TempData["Success"] = "Perfil de calibração criado.";
        return RedirectToAction(nameof(Calibration));
    }

    [HttpGet("/Labels/Calibration/{id:guid}")]
    public async Task<IActionResult> CalibrationDetails(Guid id, CancellationToken ct)
    {
        using var db=await OpenAsync();
        var profile=await db.QuerySingleOrDefaultAsync<LabelPrintProfileInput>(new CommandDefinition(ProfileSelect+" and id=@id",new{tid=TenantId,id},cancellationToken:ct));
        return profile is null ? NotFound() : View("Calibration",await LoadCalibrationPageAsync(profile,ct));
    }

    [HttpPost("/Labels/Calibration/{id:guid}/Update"),ValidateAntiForgeryToken]
    public async Task<IActionResult> CalibrationUpdate(Guid id,LabelPrintProfileInput input,CancellationToken ct)
    {
        if(UserId is null)return Unauthorized(); if(!ModelState.IsValid){input.Id=id;return View("Calibration",await LoadCalibrationPageAsync(input,ct));}
        using var db=await OpenAsync();
        var changed=await db.ExecuteAsync(new CommandDefinition("""update ged.label_print_profile set profile_name=@ProfileName,printer_name=nullif(@PrinterName,''),paper_size=@PaperSize,orientation=@Orientation,margin_top_mm=@MarginTopMm,margin_left_mm=@MarginLeftMm,offset_x_mm=@OffsetXMm,offset_y_mm=@OffsetYMm,scale_percent=@ScalePercent,label_gap_x_mm=@LabelGapXMm,label_gap_y_mm=@LabelGapYMm,notes=nullif(@Notes,''),updated_at=now() where tenant_id=@tid and id=@id and reg_status='A'""",new{tid=TenantId,id,input.ProfileName,input.PrinterName,input.PaperSize,input.Orientation,input.MarginTopMm,input.MarginLeftMm,input.OffsetXMm,input.OffsetYMm,input.ScalePercent,input.LabelGapXMm,input.LabelGapYMm,input.Notes},cancellationToken:ct));
        if(changed==0)return NotFound(); TempData["Success"]="Perfil atualizado."; return RedirectToAction(nameof(CalibrationDetails),new{id});
    }

    [HttpPost("/Labels/Calibration/{id:guid}/SetDefault"),ValidateAntiForgeryToken]
    public async Task<IActionResult> CalibrationSetDefault(Guid id,CancellationToken ct){using var db=await OpenAsync();using var tx=db.BeginTransaction();await db.ExecuteAsync("update ged.label_print_profile set is_default=false,updated_at=now() where tenant_id=@tid",new{tid=TenantId},tx);var changed=await db.ExecuteAsync("update ged.label_print_profile set is_default=true,updated_at=now() where tenant_id=@tid and id=@id and reg_status='A'",new{tid=TenantId,id},tx);tx.Commit();if(changed==0)return NotFound();return RedirectToAction(nameof(Calibration));}

    [HttpGet("/Labels/Calibration/{id:guid}/TestPage")]
    public async Task<IActionResult> CalibrationTestPage(Guid id,CancellationToken ct){using var db=await OpenAsync();var profile=await db.QuerySingleOrDefaultAsync<LabelPrintProfileInput>(new CommandDefinition(ProfileSelect+" and id=@id",new{tid=TenantId,id},cancellationToken:ct));return profile is null?NotFound():View("CalibrationTestPage",profile);}
    [HttpGet("/Labels/Calibration/{id:guid}/PrintTest")]
    public Task<IActionResult> CalibrationPrintTest(Guid id,CancellationToken ct)=>CalibrationTestPage(id,ct);

    [HttpGet("/Labels/PrintSheet")]
    public async Task<IActionResult> PrintSheet(CancellationToken ct){await PopulateProfilesAsync(ct);return View(new LabelSheetInput());}
    [HttpPost("/Labels/PrintSheet/Preview"),ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintSheetPreview(LabelSheetInput input,CancellationToken ct){if(!ModelState.IsValid){await PopulateProfilesAsync(ct);return View("PrintSheet",input);}ViewBag.Profile=await ResolveProfileAsync(input.ProfileId,ct);return View("PrintSheetPreview",input);}
    [HttpPost("/Labels/PrintSheet/Print"),ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintSheetPrint(LabelSheetInput input,CancellationToken ct){ViewBag.Profile=await ResolveProfileAsync(input.ProfileId,ct);ViewBag.AutoPrint=true;return View("PrintSheetPreview",input);}

    [HttpGet("/Labels/Quality")]
    public IActionResult Quality()=>View(BuildQualityRows());
    [HttpPost("/Labels/Quality/Validate"),ValidateAntiForgeryToken]
    public IActionResult QualityValidate(){TempData["Success"]="Validação visual executada nos modelos publicados.";return View("Quality",BuildQualityRows());}

    [HttpGet]
    public IActionResult TestSheet()=>RedirectToAction(nameof(Calibration));

    private const string ProfileSelect="select id Id,profile_name ProfileName,printer_name PrinterName,paper_size PaperSize,orientation Orientation,margin_top_mm MarginTopMm,margin_left_mm MarginLeftMm,offset_x_mm OffsetXMm,offset_y_mm OffsetYMm,scale_percent ScalePercent,label_gap_x_mm LabelGapXMm,label_gap_y_mm LabelGapYMm,is_default IsDefault,notes Notes from ged.label_print_profile where tenant_id=@tid and reg_status='A'";
    private async Task<LabelCalibrationPageViewModel> LoadCalibrationPageAsync(LabelPrintProfileInput? form,CancellationToken ct){using var db=await OpenAsync();var profiles=(await db.QueryAsync<LabelPrintProfileInput>(new CommandDefinition(ProfileSelect+" order by is_default desc,created_at desc",new{tid=TenantId},cancellationToken:ct))).AsList();return new(){Profiles=profiles,Form=form??new LabelPrintProfileInput(),ValidatedTemplates=StudioTemplates.Count,VisualAlerts=0};}
    private async Task PopulateProfilesAsync(CancellationToken ct){using var db=await OpenAsync();ViewBag.Profiles=(await db.QueryAsync<LabelPrintProfileInput>(new CommandDefinition(ProfileSelect+" order by is_default desc,profile_name",new{tid=TenantId},cancellationToken:ct))).AsList();}
    private async Task<LabelPrintProfileInput> ResolveProfileAsync(Guid? id,CancellationToken ct){using var db=await OpenAsync();return await db.QuerySingleOrDefaultAsync<LabelPrintProfileInput>(new CommandDefinition(ProfileSelect+" and (@id is null and is_default or id=@id) order by is_default desc limit 1",new{tid=TenantId,id},cancellationToken:ct))??new LabelPrintProfileInput{ProfileName="Padrão do navegador"};}
    private static IReadOnlyList<LabelQualityRow> BuildQualityRows()=>StudioTemplates.Select(t=>new LabelQualityRow{TemplateCode=t.Code,TemplateName=t.Name,Checks=["Borda presente","Fonte legível","Controle visível","Volume visível quando aplicável","LOCALIZAÇÃO presente","QR Code com tamanho mínimo","Campos obrigatórios não vazios","Texto contido na área","Dimensão definida","Modo de impressão limpo"],Alerts=[]}).ToList();

    [HttpGet]
    public Task<IActionResult> Batch(CancellationToken ct,string subjectType="BOX")=>BatchPrint(ct,subjectType);

    [HttpGet("/Labels/Printable/{printJobId:guid}")]
    public Task<IActionResult> Printable(Guid printJobId,CancellationToken ct)=>PrintPreview(printJobId,ct);

    [HttpGet]
    public IActionResult PrintablePreview()=>RedirectToAction(nameof(PrintWizard));

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet("/Labels/Demo")]
    public IActionResult Demo()
    {
        var samples = Url.Action(nameof(DemoSamples))!;
        return View(new LabelsDemoViewModel(new List<LabelsDemoCard>
        {
            new("LocDesk Pasta Padrão", LabelTemplateCode.LocDeskFolder, "Homologado", Url.Action(nameof(TemplatePreview), new { code=LabelTemplateCode.LocDeskFolder })!, samples, "folder"),
            new("LocDesk Caixa Padrão", LabelTemplateCode.LocDeskBox, "Homologado", Url.Action(nameof(TemplatePreview), new { code=LabelTemplateCode.LocDeskBox })!, samples, "box"),
            new("LocDesk Pasta HOL", LabelTemplateCode.LocDeskFolderHol, "Homologado", Url.Action(nameof(TemplatePreview), new { code=LabelTemplateCode.LocDeskFolderHol })!, samples, "hol"),
            new("Etiqueta Padrão de Caixa", LabelTemplateCode.FactoryBox, "Validado", Url.Action(nameof(TemplatePreview), new { code=LabelTemplateCode.FactoryBox })!, samples, "factory"),
            new("Etiqueta Padrão de Documento", LabelTemplateCode.FactoryDocument, "Validado", Url.Action(nameof(TemplatePreview), new { code=LabelTemplateCode.FactoryDocument })!, samples, "document"),
            new("Impressão em Lote", "LABEL_BATCH", "Operacional", Url.Action(nameof(Batch))!, samples, "batch"),
            new("Calibração", "PRINT_CALIBRATION", "Validado", Url.Action(nameof(Calibration))!, samples, "calibration"),
            new("Scanner QR", "QR_SCANNER", "Online", "/Labels/Scanner", samples, "scanner"),
            new("Histórico", "PRINT_HISTORY", "Auditável", Url.Action(nameof(History))!, samples, "history"),
            new("Rastreabilidade", "LABEL_TRACE", "Auditável", "/Labels/Trace", samples, "trace")
        }));
    }

    [HttpGet("/Labels/Demo/Samples")]
    public IActionResult DemoSamples() => View(new LabelsDemoSamplesViewModel(LabelsDemoData.Standard(), LabelsDemoData.Hol(),
        _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}/Labels/Demo")));

    [HttpGet("/Labels/Demo/Acceptance")]
    public IActionResult DemoAcceptance() => View();

    [HttpGet("/Labels/VisualReview")]
    public IActionResult VisualReview() => View(StudioTemplates);

    [HttpGet("/Labels/VisualChecklist")]
    public IActionResult VisualChecklist() => View(BuildQualityRows());

    [HttpGet]
    public async Task<IActionResult> PrintWizard(string? subjectType, Guid? subjectId, string? mode, string? templateCode, CancellationToken ct)
    {
        subjectType = subjectType?.ToUpperInvariant() ?? LabelSubjectType.Box;
        mode = mode?.ToUpperInvariant() ?? LabelPrintMode.Factory;
        var options = await _catalog.GetTemplatesAsync(TenantId,subjectType,mode,ct);
        if (_catalog.IsTemporaryCatalog) ViewBag.CatalogMigrationWarning = "As migrations de modelos de etiqueta ainda não foram aplicadas. O sistema está usando catálogo temporário.";
        if (string.IsNullOrWhiteSpace(templateCode) || !await _catalog.IsCompatibleAsync(TenantId,templateCode,subjectType,ct) || !options.Any(x=>x.Code==templateCode)) templateCode=options.FirstOrDefault()?.Code ?? "";
        var model = new LabelPrintWizardInputModel { SubjectType=subjectType, SubjectId=subjectId, PrintMode=mode, TemplateCode=templateCode };
        await PopulatePrintWizardLookupsAsync(model, ct);
        return View(model);
    }

    [HttpGet]
    public IActionResult Print()
    {
        TempData["Info"] = "Use o assistente para selecionar o tipo, o modelo e a origem da etiqueta antes de imprimir.";
        return RedirectToAction(nameof(PrintWizard));
    }

    [HttpGet]
    public async Task<IActionResult> Templates(string? subjectType, string? mode, CancellationToken ct)
        => Json(await _catalog.GetTemplatesAsync(TenantId,subjectType?.ToUpperInvariant() ?? LabelSubjectType.Box,mode?.ToUpperInvariant(),ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(LabelPrintWizardInputModel input, CancellationToken ct)
    {
        LogLogoSelection(nameof(Preview), input);
        return await BuildLabelRenderModelAsync(input, isRealPrint: false, ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintPreview(LabelPrintWizardInputModel input, CancellationToken ct)
    {
        LogLogoSelection(nameof(PrintPreview), input);
        return await BuildLabelRenderModelAsync(input, isRealPrint: false, ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Print(LabelPrintWizardInputModel input, CancellationToken ct)
    {
        LogLogoSelection(nameof(Print), input);
        return await BuildLabelRenderModelAsync(input, isRealPrint: true, ct);
    }

    private void LogLogoSelection(string action, LabelPrintWizardInputModel input) =>
        _logger.LogInformation(
            "Label render logo selection: Action={Action}, TemplateCode={TemplateCode}, SelectedLogoAssetIdPresent={SelectedLogoAssetIdPresent}",
            action, input.TemplateCode, input.SelectedLogoAssetId.HasValue);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintBatch(LabelBatchPrintInputModel input, CancellationToken ct)
    {
        if (input.SubjectIds.Count==0) ModelState.AddModelError(nameof(input.SubjectIds),"Selecione ao menos uma etiqueta.");
        if (!await _catalog.IsCompatibleAsync(TenantId,input.TemplateCode,input.SubjectType,ct)) ModelState.AddModelError(nameof(input.TemplateCode),"O modelo selecionado não é compatível com o tipo de origem escolhido.");
        if (!ModelState.IsValid) return await PrintWizard(input.SubjectType,null,input.PrintMode,input.TemplateCode,ct);
        foreach(var id in input.SubjectIds) {
            var wizard=new LabelPrintWizardInputModel { SubjectType=input.SubjectType,SubjectId=id,PrintMode=input.PrintMode,TemplateCode=input.TemplateCode,Copies=input.Copies,ReprintReason=input.ReprintReason };
            var result=await BuildLabelRenderModelAsync(wizard,true,ct); if(result is NotFoundResult) return result;
        }
        TempData["Success"]=$"{input.SubjectIds.Count} etiqueta(s) registradas no lote.";
        return RedirectToAction(nameof(History));
    }

    private async Task<IActionResult> BuildLabelRenderModelAsync(LabelPrintWizardInputModel input, bool isRealPrint, CancellationToken ct)
    {
        var register = isRealPrint;
        input.SubjectType = (input.SubjectType ?? string.Empty).Trim().ToUpperInvariant();
        input.PrintMode = (input.PrintMode ?? string.Empty).Trim().ToUpperInvariant();
        input.TemplateCode = (input.TemplateCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input.TemplateCode)) ModelState.AddModelError(nameof(input.TemplateCode), "Selecione um modelo de etiqueta.");
        if (!LabelSubjectType.IsValid(input.SubjectType)) ModelState.AddModelError(nameof(input.SubjectType), "Selecione um tipo de origem válido.");
        if (input.SubjectType != LabelSubjectType.Manual && input.SubjectId is null) ModelState.AddModelError(nameof(input.SubjectId), "Selecione uma caixa, documento ou lote antes de imprimir.");
        if (input.Copies <= 0) ModelState.AddModelError(nameof(input.Copies), "A quantidade de cópias deve ser maior que zero.");

        LabelTemplateOption? template = null;
        if (ModelState.IsValid)
        {
            template = await _catalog.TryGetTemplateAsync(TenantId, input.TemplateCode, ct);
            if (template is null)
                ModelState.AddModelError(nameof(input.TemplateCode), "O modelo de etiqueta selecionado não foi encontrado. Atualize a lista de modelos ou aplique as migrations obrigatórias em Prontidão do Banco.");
            else if (!string.Equals(template.SubjectType, input.SubjectType, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(input.TemplateCode), "O modelo selecionado não é compatível com o tipo de origem escolhido.");
            else if (!string.IsNullOrWhiteSpace(input.PrintMode) && !string.Equals(template.Mode, input.PrintMode, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(input.TemplateCode), "O modelo selecionado não pertence ao modo de impressão escolhido.");
        }
        if (!ModelState.IsValid || template is null)
        {
            await PopulatePrintWizardLookupsAsync(input, ct);
            return View("PrintWizard", input);
        }
        if (input.PrintMode==LabelPrintMode.Custom) {
            input.CustomFields.LogoSelection=input.LogoSelection; input.CustomFields.SelectedLogoAssetId=input.SelectedLogoAssetId; input.CustomFields.LogoWidthMm=input.LogoWidthMm; input.CustomFields.LogoHeightMm=input.LogoHeightMm; input.CustomFields.PreserveLogoAspectRatio=input.PreserveAspectRatio; input.CustomFields.LogoFitMode=input.LogoFitMode; input.CustomFields.LogoPosition=input.LogoPosition;
            IActionResult? loaded = input.SubjectType switch
            {
                LabelSubjectType.Box when input.SubjectId is Guid box => await LocDeskBoxFromPhysical(box, input.SelectedLogoAssetId, input.LogoSelection, input.LogoWidthMm, input.LogoHeightMm, input.PreserveAspectRatio, input.LogoFitMode, ct),
                LabelSubjectType.Document when input.SubjectId is Guid doc => await LocDeskFolderFromDocument(doc, template.Code, input.SelectedLogoAssetId, input.LogoSelection, input.LogoWidthMm, input.LogoHeightMm, input.PreserveAspectRatio, input.LogoFitMode, ct),
                _ => null
            };
            if (loaded is not ViewResult { Model: LocDeskLabelInputModel locDesk }) return loaded ?? NotFound();
            locDesk.TemplateCode=template.Code;
            locDesk.LogoPosition=input.LogoPosition ?? "TOP_LEFT";
            InovaGed.Application.Labels.LabelTraceIssued? locDeskIssued=null;
            if (register)
            {
                try { locDeskIssued=await RegisterLocDeskAsync(locDesk, input.SubjectId!.Value, input.ReprintReason, ct); }
                catch (InvalidOperationException ex) { ModelState.AddModelError(nameof(input.ReprintReason), ex.Message); await PopulatePrintWizardLookupsAsync(input,ct); return View("PrintWizard",input); }
            }
            return await RenderLocDesk(locDesk,register,ct,locDeskIssued);
        }
        using var db=await OpenAsync();
        object? subject = input.SubjectType switch
        {
            LabelSubjectType.Box => await LoadBoxLabelAsync(db, input.SubjectId!.Value),
            LabelSubjectType.Document => await LoadDocumentLabelAsync(db, input.SubjectId!.Value),
            LabelSubjectType.Batch => await LoadBatchLabelAsync(db, input.SubjectId!.Value),
            _ => null
        };
        if(subject is null) { ModelState.AddModelError(nameof(input.SubjectId), "Não foi possível localizar a origem selecionada."); await PopulatePrintWizardLookupsAsync(input, ct); return View("PrintWizard", input); }
        var wasPrinted = await db.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.label_print_history where tenant_id=@tid and label_subject_type=@type and label_subject_id=@id)", new { tid=TenantId, type=input.SubjectType, id=input.SubjectId }, cancellationToken:ct));
        if (register && wasPrinted && string.IsNullOrWhiteSpace(input.ReprintReason)) { ModelState.AddModelError(nameof(input.ReprintReason), "Informe o motivo da reimpressão para preservar a rastreabilidade."); await PopulatePrintWizardLookupsAsync(input, ct); return View("PrintWizard", input); }
        var resolvedLogo=await _logoResolver.ResolveAsync(TenantId,template.Code,input.LogoSelection,input.SelectedLogoAssetId,input.LogoWidthMm,input.LogoHeightMm,input.PreserveAspectRatio,input.LogoFitMode,input.LogoPosition,input.LogoOffsetXmm??0,input.LogoOffsetYmm??0,ct);
        var printLogo=PrintLogoViewModelMapper.FromResolved(resolvedLogo);
        _logger.LogInformation(
            "Labels {Operation}: TemplateCode={TemplateCode}; SelectedLogoAssetId preenchido={HasSelectedLogo}; HasLogo={HasLogo}; ImageLoaded={ImageLoaded}; DataUri={HasDataUri}",
            register ? "Print" : "Preview", template.Code, input.SelectedLogoAssetId.HasValue,
            printLogo.HasLogo, printLogo.ImageLoaded,
            printLogo.PrintImageSource?.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) == true);
        InovaGed.Application.Labels.LabelTraceIssued? issued=null;
        if(register) { if(UserId is not Guid uid) return Unauthorized(); var snapshot=new { printMode=input.PrintMode,templateCode=template.Code,templateName=template.Name,templateVersion=template.Version,isDesignerTemplate=template.Id is not null && !template.IsSystemTemplate,subjectType=input.SubjectType,subjectId=input.SubjectId,copies=input.Copies,controlNumber=(string?)null,location=(string?)null,logoAssetId=printLogo.AssetId,logoBrandName=printLogo.BrandName,logoWidthMm=printLogo.WidthMm,logoHeightMm=printLogo.HeightMm,logoFitMode=printLogo.FitMode,logoPosition=printLogo.Position,printedFields=subject };
            try { issued=await _printRegistrar.RegisterAsync(new(TenantId,uid,input.SubjectType,input.SubjectId!.Value,template.Code,_payloadBuilder.Build(snapshot),HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),input.ReprintReason),ct); } catch(InvalidOperationException ex) { ModelState.AddModelError(nameof(input.ReprintReason),ex.Message); ViewBag.Templates=await _catalog.GetTemplatesAsync(TenantId,input.SubjectType,input.PrintMode,ct); ViewBag.SubjectOptions=await LoadSubjectOptionsAsync(input.SubjectType,ct); return View("PrintWizard",input); } }
        var qrPath=issued?.ShortUrl??$"/Labels/Trace/{input.SubjectId}"; ViewBag.TraceCode=issued?.Trace.TraceCode; ViewBag.QrSvg=_qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}{qrPath}"); ViewBag.PrintRegistered=register; ViewBag.IsPrintPage=register; ViewBag.Copies=input.Copies; ViewBag.PrintLogo=printLogo; ViewBag.PrintLogoWarning=printLogo.HasLogo&&!printLogo.ImageLoaded?printLogo.LoadError:null;
        return View(template.ViewName,subject);
    }

    private async Task<IReadOnlyList<SelectOptionViewModel>> LoadSubjectOptionsAsync(string subjectType, CancellationToken ct)
    {
        using var db = await OpenAsync();
        var sql = subjectType switch
        {
            LabelSubjectType.Box => "select id::text Value, concat('Caixa ',box_no,coalesce(' · '||nullif(label_code,''),''),coalesce(' · '||nullif(notes,''),'')) Label from ged.box where tenant_id=@tid and reg_status='A' order by box_no limit 200",
            LabelSubjectType.Document => "select id::text Value, concat(coalesce(nullif(code,''),'Sem código'),' · ',title) Label from ged.document where tenant_id=@tid and reg_status='A' order by created_at desc limit 200",
            LabelSubjectType.Batch => "select id::text Value, concat('Lote ',batch_no,coalesce(' · '||nullif(notes,''),'')) Label from ged.batch where tenant_id=@tid and reg_status='A' order by reg_date desc limit 200",
            _ => null
        };
        if (sql is null) return Array.Empty<SelectOptionViewModel>();
        var rows = await db.QueryAsync<SelectOptionViewModel>(new CommandDefinition(sql,new {tid=TenantId},cancellationToken:ct));
        return rows.AsList();
    }

    private async Task PopulatePrintWizardLookupsAsync(LabelPrintWizardInputModel model, CancellationToken ct)
    {
        var safeType = LabelSubjectType.IsValid(model.SubjectType) ? model.SubjectType : LabelSubjectType.Box;
        var safeMode = LabelPrintMode.IsValid(model.PrintMode) ? model.PrintMode : LabelPrintMode.Factory;
        model.SubjectType = safeType;
        model.PrintMode = safeMode;
        ViewBag.Templates = await _catalog.GetTemplatesAsync(TenantId, safeType, safeMode, ct);
        ViewBag.SubjectOptions = await LoadSubjectOptionsAsync(safeType, ct);
        ViewBag.Boxes = safeType == LabelSubjectType.Box ? ViewBag.SubjectOptions : Array.Empty<SelectOptionViewModel>();
        ViewBag.Documents = safeType == LabelSubjectType.Document ? ViewBag.SubjectOptions : Array.Empty<SelectOptionViewModel>();
        ViewBag.Batches = safeType == LabelSubjectType.Batch ? ViewBag.SubjectOptions : Array.Empty<SelectOptionViewModel>();
        ViewBag.Warnings = _catalog.IsTemporaryCatalog ? new[] { "Catálogo temporário de modelos em uso." } : Array.Empty<string>();
        using (var db = await OpenAsync())
        {
            ViewBag.PrintProfiles = (await db.QueryAsync<LabelPrintProfileInput>(new CommandDefinition(ProfileSelect + " order by is_default desc,profile_name", new { tid=TenantId }, cancellationToken:ct))).AsList();
            ViewBag.SelectedPrintProfile = await ResolveProfileAsync(model.PrintProfileId, ct);
            var brandAssets = (await db.QueryAsync<InovaGed.Web.Models.Branding.BrandAssetVm>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,original_file_name OriginalFileName,content_type ContentType,file_extension FileExtension,file_size_bytes FileSizeBytes,storage_relative_path StorageRelativePath,is_default IsDefault,status,created_at CreatedAt from ged.brand_asset where tenant_id=@tid and status='ACTIVE' and reg_status='A' order by is_default desc,asset_name",new{tid=TenantId},cancellationToken:ct))).AsList();
            ViewBag.BrandAssets = brandAssets;
            model.LogoOptions = brandAssets.Select(asset => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem($"{asset.AssetName} — {asset.BrandName}", asset.Id.ToString(), asset.Id == model.SelectedLogoAssetId)).ToList();
            ViewBag.PrintBrandingProfiles = await db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.print_branding_profile') is not null", cancellationToken:ct))
                ? (await db.QueryAsync<InovaGed.Web.Models.Branding.PrintBrandingProfileVm>(new CommandDefinition("select id,profile_name ProfileName,is_default IsDefault,status from ged.print_branding_profile where tenant_id=@tid and status='ACTIVE' and reg_status='A' order by is_default desc,profile_name",new{tid=TenantId},cancellationToken:ct))).AsList()
                : new List<InovaGed.Web.Models.Branding.PrintBrandingProfileVm>();
        }
        if (_catalog.IsTemporaryCatalog) ViewBag.CatalogMigrationWarning = "As migrations de modelos de etiqueta ainda não foram aplicadas. O sistema está usando catálogo temporário.";
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

    private async Task<dynamic?> LoadBatchLabelAsync(System.Data.IDbConnection db, Guid batchId)
        => await db.QueryFirstOrDefaultAsync("""
select b.id, b.batch_no, b.notes, b.reg_date, count(bi.id) as document_count
from ged.batch b
left join ged.batch_item bi on bi.tenant_id=b.tenant_id and bi.batch_id=b.id and bi.reg_status='A'
where b.tenant_id=@tid and b.id=@batchId and b.reg_status='A'
group by b.id, b.batch_no, b.notes, b.reg_date
""", new { tid = TenantId, batchId });

    [HttpGet]
    public IActionResult LocDesk()
        => View(CreateLocDeskPreviewDefaults());

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
            var issued=await RegisterLocDeskAsync(input, subjectId, input.ReprintReason, ct);
            return await RenderLocDesk(input,true,ct,issued);
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
    public async Task<IActionResult> LocDeskBox(Guid boxId, Guid? selectedLogoAssetId, string? logoSelection,
        decimal? logoWidthMm, decimal? logoHeightMm, bool preserveLogoAspectRatio = true,
        string? logoFitMode = null, CancellationToken ct = default)
        => await LocDeskBoxFromPhysical(boxId, selectedLogoAssetId, logoSelection, logoWidthMm,
            logoHeightMm, preserveLogoAspectRatio, logoFitMode, ct);

    [HttpGet]
    public async Task<IActionResult> LocDeskBoxFromPhysical(Guid boxId, Guid? selectedLogoAssetId = null,
        string? logoSelection = null, decimal? logoWidthMm = null, decimal? logoHeightMm = null,
        bool preserveLogoAspectRatio = true, string? logoFitMode = null, CancellationToken ct = default)
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
        ApplyWizardLogo(model, selectedLogoAssetId, logoSelection, logoWidthMm, logoHeightMm,
            preserveLogoAspectRatio, logoFitMode);
        return View("LocDesk", model);
    }

    [HttpGet]
    public async Task<IActionResult> LocDeskFolder(Guid docId, string? templateCode, Guid? selectedLogoAssetId,
        string? logoSelection, decimal? logoWidthMm, decimal? logoHeightMm,
        bool preserveLogoAspectRatio = true, string? logoFitMode = null, CancellationToken ct = default)
        => await LocDeskFolderFromDocument(docId, templateCode, selectedLogoAssetId, logoSelection,
            logoWidthMm, logoHeightMm, preserveLogoAspectRatio, logoFitMode, ct);

    [HttpGet]
    public async Task<IActionResult> LocDeskFolderFromDocument(Guid docId, string? templateCode,
        Guid? selectedLogoAssetId = null, string? logoSelection = null, decimal? logoWidthMm = null,
        decimal? logoHeightMm = null, bool preserveLogoAspectRatio = true, string? logoFitMode = null,
        CancellationToken ct = default)
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
        ApplyWizardLogo(model, selectedLogoAssetId, logoSelection, logoWidthMm, logoHeightMm,
            preserveLogoAspectRatio, logoFitMode);
        model.TemplateCode = templateCode == LabelTemplateCode.LocDeskFolderHol ? templateCode : LabelTemplateCode.LocDeskFolder;
        return View("LocDesk", model);
    }

    private static void ApplyWizardLogo(LocDeskLabelInputModel model, Guid? assetId, string? selection,
        decimal? widthMm, decimal? heightMm, bool preserveAspectRatio, string? fitMode)
    {
        model.SelectedLogoAssetId = assetId;
        model.LogoSelection = assetId.HasValue ? "SELECTED" : selection ?? model.LogoSelection;
        model.LogoWidthMm = widthMm;
        model.LogoHeightMm = heightMm;
        model.PreserveLogoAspectRatio = preserveAspectRatio;
        if (!string.IsNullOrWhiteSpace(fitMode)) model.LogoFitMode = fitMode;
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

    private async Task<IActionResult> RenderLocDesk(LocDeskLabelInputModel input,bool registered,CancellationToken ct,InovaGed.Application.Labels.LabelTraceIssued? issued=null)
    {
        var qr = issued is null ? CreateLocDeskQr(input, input.BoxId ?? input.DocumentId) : _qrCodes.CreateTrackingSvg($"{Request.Scheme}://{Request.Host}{issued.ShortUrl}");
        ViewBag.TraceCode=issued?.Trace.TraceCode;
        var resolvedLogo=await _logoResolver.ResolveAsync(TenantId,ResolveLocDeskTemplateCode(input),input.LogoSelection,input.SelectedLogoAssetId,input.LogoWidthMm,input.LogoHeightMm,input.PreserveLogoAspectRatio,input.LogoFitMode,input.LogoPosition,0,0,ct);
        var logo=PrintLogoViewModelMapper.FromResolved(resolvedLogo);
        ViewBag.IsPrintPage=registered;
        var model = new LocDeskLabelRenderModel { Label=input,QrSvg=qr,PrintRegistered=registered,Template=await LoadLocDeskTemplate(input,ct),PrintLogo=logo,PrintLogoWarning=logo.HasLogo&&!logo.ImageLoaded?logo.LoadError:null };
        return View(LocDeskViewName(input), model);
    }

    private async Task<InovaGed.Application.Labels.LabelTemplateDetails?> LoadLocDeskTemplate(LocDeskLabelInputModel input,CancellationToken ct)
    {
        var code=ResolveLocDeskTemplateCode(input);
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

    private async Task<InovaGed.Application.Labels.LabelTraceIssued> RegisterLocDeskAsync(LocDeskLabelInputModel input, Guid subjectId, string? reason, CancellationToken ct)
    {
        if (UserId is not Guid userId) throw new UnauthorizedAccessException("Usuário autenticado obrigatório.");
        var type = input.BoxId.HasValue ? "BOX" : input.DocumentId.HasValue ? "DOCUMENT" : "MANUAL_LABEL";
        var template = ResolveLocDeskTemplateCode(input);
        var option=await _catalog.GetTemplateAsync(TenantId,template,ct);
        var details=option.Id is Guid templateId?await _templateManager.GetAsync(TenantId,templateId,ct):null;
        var snapshot=new { printMode=option.Mode,templateCode=option.Code,templateName=option.Name,templateVersion=details?.Template.Version??1,isDefault=option.IsDefault,configuration=details,input };
        return await _printRegistrar.RegisterAsync(new LabelPrintRequest(TenantId,userId,type,subjectId,template,_payloadBuilder.Build(snapshot),HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),reason),ct);
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

    private static string ResolveLocDeskTemplateCode(LocDeskLabelInputModel input) =>
        input.LabelKind == LocDeskLabelKind.Box ? LabelTemplateCode.LocDeskBox :
        input.TemplateCode == LabelTemplateCode.LocDeskFolderHol ? LabelTemplateCode.LocDeskFolderHol : LabelTemplateCode.LocDeskFolder;

    private static string LocDeskViewName(LocDeskLabelInputModel input) => ResolveLocDeskTemplateCode(input) switch
    {
        LabelTemplateCode.LocDeskBox => "LocDeskBoxLabel",
        LabelTemplateCode.LocDeskFolderHol => "LocDeskFolderHolLabel",
        _ => "LocDeskFolderLabel"
    };

    private static LocDeskLabelInputModel CreateLocDeskPreviewDefaults() => new()
    {
        TemplateCode = LabelTemplateCode.LocDeskFolderHol,
        Contract = "Hosp. Ophir Loyola",
        MedicalRecordNumber = "100.334",
        ControlNumber = "199",
        VolumeNumber = 1,
        VolumeTotal = 1,
        Subject = "PRONTUÁRIO nº: 100.334",
        Details = "DAME - ALTA MEDICA",
        Activity = "FIM",
        Classification = "HOL.132.3 - LAUDO DE PROCEDIMENTOS DIAGNÓSTICOS",
        Support = "1. PAPEL",
        PeriodStart = new DateTime(2017, 7, 15),
        PeriodEnd = new DateTime(2017, 9, 25),
        DocumentPeriod = "15/07/2017 A 25/09/2017",
        CurrentPhase = "2. GUARDA INTERMEDIÁRIA",
        EliminationForecast = "0. GUARDA PERMANENTE",
        EliminationStatus = "0. GUARDA PERMANENTE",
        LedNumber = "N/A",
        Location = "LOC.AN.___.E___.P___"
    };

    [HttpGet("/Labels/Trace/{id:guid}")]
    public async Task<IActionResult> Trace(Guid id, CancellationToken ct)
    {
        using var db=await OpenAsync();
        var draft=await db.QueryFirstOrDefaultAsync(new CommandDefinition("select * from ged.locdesk_label_draft where tenant_id=@tid and id=@id and reg_status='A'",new {tid=TenantId,id},cancellationToken:ct));
        return draft is null ? NotFound() : View(draft);
    }

    [HttpGet]
    public async Task<IActionResult> History(string? q, string? user, string? mode, string? template, string? type, DateTime? startDate, DateTime? endDate)
    {
        using var db = await OpenAsync();

        q = (q ?? "").Trim();
        ViewBag.Q = q;
        user = (user ?? "").Trim();
        ViewBag.User = user;
        ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
        ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
        mode=(mode??"").Trim().ToUpperInvariant(); template=(template??"").Trim().ToUpperInvariant(); type=(type??"").Trim().ToUpperInvariant();

        if (!await db.ExecuteScalarAsync<bool>("select to_regclass('ged.label_print_history') is not null"))
        {
            ViewBag.SchemaPending = true;
            return View(Array.Empty<dynamic>());
        }

        var sql = new StringBuilder(@"
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
    coalesce(lp.snapshot_json->>'templateName',case lp.template_code when 'FACTORY_BOX_V1' then 'Padrão do Sistema - Caixa' when 'FACTORY_DOCUMENT_V1' then 'Padrão do Sistema - Documento/Pasta' when 'LOCDESK_CAIXA_V1' then 'LocDesk - Caixa' when 'LOCDESK_PASTA_V1' then 'LocDesk - Pasta' when 'LOCDESK_PASTA_HOL_V1' then 'LocDesk - Pasta HOL' else lp.template_code end) as template_name,
    lp.snapshot_json->>'templateVersion' as template_version,
    coalesce((lp.snapshot_json->>'isDesignerTemplate')::boolean,false) as is_designer_template,
    lp.snapshot_sha256,
    lp.reprint_reason,
    lp.snapshot_json->>'controlNumber' as control_number,
    lp.snapshot_json->>'location' as location,
    lp.snapshot_json->>'subject' as locdesk_subject,
    lp.snapshot_json->>'classification' as locdesk_classification
    ,coalesce(nullif(lp.snapshot_json->>'copies','')::int,1) as copies
    ,'PRINTED' as print_status
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
");
        var parameters = new DynamicParameters();
        parameters.Add("tid", TenantId, DbType.Guid);
        if (startDate.HasValue) { sql.AppendLine("and lp.printed_at >= @startDate"); parameters.Add("startDate", startDate.Value.Date, DbType.DateTime); }
        if (endDate.HasValue) { sql.AppendLine("and lp.printed_at < @endDateExclusive"); parameters.Add("endDateExclusive", endDate.Value.Date.AddDays(1), DbType.DateTime); }
        if (!string.IsNullOrWhiteSpace(type)) { sql.AppendLine("and lp.label_subject_type = @type"); parameters.Add("type", type, DbType.String); }
        if (!string.IsNullOrWhiteSpace(template)) { sql.AppendLine("and lp.template_code = @template"); parameters.Add("template", template, DbType.String); }
        if (!string.IsNullOrWhiteSpace(mode)) { sql.AppendLine("and @mode = coalesce(lp.snapshot_json->>'printMode', case when lp.template_code like 'LOCDESK%' then 'CUSTOM' else 'FACTORY' end)"); parameters.Add("mode", mode, DbType.String); }
        if (!string.IsNullOrWhiteSpace(user)) { sql.AppendLine("and coalesce(u.name,'') ilike @user"); parameters.Add("user", $"%{user}%", DbType.String); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql.AppendLine(@"and (
    coalesce(lp.label_subject_type,'') ilike @q
    or coalesce(b.label_code,'') ilike @q
    or coalesce(b.box_no::text,'') ilike @q
    or coalesce(d.code,'') ilike @q
    or coalesce(d.title,'') ilike @q
    or coalesce(u.name,'') ilike @q
    or coalesce(lp.template_code,'') ilike @q
    or coalesce(lp.snapshot_json->>'controlNumber','') ilike @q
    or coalesce(lp.snapshot_json->>'location','') ilike @q
    or coalesce(lp.snapshot_json->>'subject','') ilike @q
    or coalesce(lp.snapshot_json->>'classification','') ilike @q
  )");
            parameters.Add("q", $"%{q}%", DbType.String);
        }
        sql.AppendLine("order by lp.printed_at desc limit 500;");
        var rows = await db.QueryAsync(sql.ToString(), parameters);

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
        return row is null?NotFound():View("HistoryDetails",row);
    }

    [HttpGet("/Labels/History/{id:guid}")]
    public Task<IActionResult> HistoryDetails(Guid id,CancellationToken ct)=>PrintDetails(id,ct);

    [HttpPost("/Labels/History/{id:guid}/Reprint"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprint(Guid id,string? justification,CancellationToken ct)
    {
        if(UserId is not Guid uid)return Unauthorized();
        if(string.IsNullOrWhiteSpace(justification)){TempData["Error"]="Informe o motivo da reimpressão para manter a rastreabilidade.";return RedirectToAction(nameof(HistoryDetails),new{id});}
        using var db=await OpenAsync();
        var row=await db.QuerySingleOrDefaultAsync(new CommandDefinition("select label_subject_type,label_subject_id,template_code,snapshot_json::text snapshot_json from ged.label_print_history where tenant_id=@tid and id=@id",new{tid=TenantId,id},cancellationToken:ct));
        if(row is null)return NotFound();
        await _printRegistrar.RegisterAsync(new(TenantId,uid,(string)row.label_subject_type,(Guid)row.label_subject_id,(string)row.template_code,(string)row.snapshot_json,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),justification.Trim()),ct);
        TempData["Success"]="Reimpressão registrada com rastreabilidade.";
        return RedirectToAction(nameof(PrintWizard),new{subjectType=(string)row.label_subject_type,subjectId=(Guid)row.label_subject_id,templateCode=(string)row.template_code});
    }
}
