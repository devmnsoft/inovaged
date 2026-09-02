using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Labels;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.FullAdminOnly)]
[Route("Labels/LogoLayout")]
public sealed class LogoLayoutController(IDbConnectionFactory factory) : GedControllerBase(factory)
{
    private static readonly IReadOnlyList<(string Code,string Name,string Kind,string Dimensions)> Catalog =
    [
        ("LOCDESK_PASTA_V1","LocDesk Pasta","Etiqueta","174 × 110 mm"), ("LOCDESK_CAIXA_V1","LocDesk Caixa","Etiqueta","174 × 110 mm"),
        ("LOCDESK_PASTA_HOL_V1","LocDesk Pasta HOL","Etiqueta","174 × 110 mm"), ("FACTORY_BOX_V1","Etiqueta Padrão de Caixa","Etiqueta","100 × 70 mm"),
        ("FACTORY_DOCUMENT_V1","Etiqueta Padrão de Documento","Etiqueta","100 × 70 mm"), ("DOCUMENT_PRINT_HEADER","Documento Impresso","Documento","A4"),
        ("DOCUMENT_REPORT","Relatório","Documento","A4"), ("ACCEPTANCE_TERM","Termo de Aceite","Documento","A4")
    ];

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        using var db=await OpenAsync(); var ready=await HasTableAsync(db,"ged","print_logo_selection");
        var configured = ready ? (await db.QueryAsync<CardRow>(new CommandDefinition("select s.context_key Code,case when s.logo_asset_id is null then null else '/Administration/BrandAssets/'||s.logo_asset_id||'/File' end LogoUrl from ged.print_logo_selection s where s.tenant_id=@tenant and s.reg_status='A'",new{tenant=TenantId},cancellationToken:ct))).ToDictionary(x=>x.Code,StringComparer.OrdinalIgnoreCase) : new Dictionary<string,CardRow>(StringComparer.OrdinalIgnoreCase);
        var cards=Catalog.Select(x=>new LogoLayoutCard(x.Code,x.Name,x.Kind,x.Dimensions,configured.GetValueOrDefault(x.Code)?.LogoUrl,configured.ContainsKey(x.Code)?"Configurada":"Usando padrão seguro")).ToList();
        return View(cards);
    }

    [HttpGet("{templateCode}")]
    public async Task<IActionResult> Edit(string templateCode,CancellationToken ct)
    { var item=Catalog.FirstOrDefault(x=>x.Code.Equals(templateCode,StringComparison.OrdinalIgnoreCase)); return string.IsNullOrEmpty(item.Code)?NotFound():View(await Load(item,ct)); }

    [HttpPost("{templateCode}/Save"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string templateCode,LogoLayoutInput input,CancellationToken ct)
    {
        var item=Catalog.FirstOrDefault(x=>x.Code.Equals(templateCode,StringComparison.OrdinalIgnoreCase)); if(string.IsNullOrEmpty(item.Code))return NotFound();
        if(!ModelState.IsValid)return View("Edit",await Load(item,ct,input)); using var db=await OpenAsync();
        if(!await HasTableAsync(db,"ged","print_logo_selection")){ModelState.AddModelError("","A migration do Logo Layout Studio ainda não foi aplicada.");return View("Edit",await Load(item,ct,input));}
        if(input.LogoAssetId is Guid asset && !await db.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.brand_asset where id=@asset and tenant_id=@tenant and status='ACTIVE' and reg_status='A')",new{asset,tenant=TenantId},cancellationToken:ct)))
        {
            ModelState.AddModelError(nameof(input.LogoAssetId), "A logo selecionada não está ativa ou não pertence a este cliente.");
            return View("Edit", await Load(item, ct, input));
        }
        await db.ExecuteAsync(new CommandDefinition("""insert into ged.print_logo_selection(tenant_id,context,context_key,logo_asset_id,width_mm,height_mm,preserve_aspect_ratio,fit_mode,position,position_x_mm,position_y_mm,margin_top_mm,margin_left_mm,apply_to_all_copies,enabled,created_by) values(@tenant,'LABEL_TEMPLATE',@key,@LogoAssetId,@WidthMm,@HeightMm,@PreserveAspectRatio,@FitMode,@Position,@PositionXMm,@PositionYMm,@MarginTopMm,@MarginLeftMm,@ApplyToAllCopies,@Enabled,@user) on conflict (tenant_id,context,context_key) where reg_status='A' do update set logo_asset_id=excluded.logo_asset_id,width_mm=excluded.width_mm,height_mm=excluded.height_mm,preserve_aspect_ratio=excluded.preserve_aspect_ratio,fit_mode=excluded.fit_mode,position=excluded.position,position_x_mm=excluded.position_x_mm,position_y_mm=excluded.position_y_mm,margin_top_mm=excluded.margin_top_mm,margin_left_mm=excluded.margin_left_mm,apply_to_all_copies=excluded.apply_to_all_copies,enabled=excluded.enabled,updated_at=now()""",new{tenant=TenantId,key=item.Code,input.LogoAssetId,input.WidthMm,input.HeightMm,input.PreserveAspectRatio,input.FitMode,input.Position,input.PositionXMm,input.PositionYMm,input.MarginTopMm,input.MarginLeftMm,input.ApplyToAllCopies,input.Enabled,user=UserId},cancellationToken:ct));
        TempData["Success"]="Configuração visual da logo salva."; return RedirectToAction(nameof(Edit),new{templateCode=item.Code});
    }

    [HttpGet("{templateCode}/Save")]
    public IActionResult SaveGetFallback(string templateCode)
    {
        if (!Catalog.Any(x => x.Code.Equals(templateCode, StringComparison.OrdinalIgnoreCase))) return NotFound();
        TempData["Warning"] = "Use o botão Salvar configuração para gravar os ajustes da logo.";
        return RedirectToAction(nameof(Edit), new { templateCode });
    }

    [HttpPost("{templateCode}/Preview"),ValidateAntiForgeryToken]
    public IActionResult Preview(string templateCode,LogoLayoutInput input)=>Catalog.Any(x=>x.Code==templateCode)?PartialView("_Preview",new LogoLayoutEditorVm{TemplateCode=templateCode,Layout=input}):NotFound();
    [HttpGet("{templateCode}/PrintTest")]
    public async Task<IActionResult> PrintTest(string templateCode,CancellationToken ct){var item=Catalog.FirstOrDefault(x=>x.Code==templateCode);return string.IsNullOrEmpty(item.Code)?NotFound():View("PrintTest",await Load(item,ct));}

    private async Task<LogoLayoutEditorVm> Load((string Code,string Name,string Kind,string Dimensions) item,CancellationToken ct,LogoLayoutInput? posted=null)
    {
        using var db=await OpenAsync(); var assets=new List<LogoLayoutAsset>(); var ready=await HasTableAsync(db,"ged","print_logo_selection")&&await HasTableAsync(db,"ged","brand_asset");
        if(await HasTableAsync(db,"ged","brand_asset")){var rows=await db.QueryAsync<AssetRow>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,is_default IsDefault,width_px WidthPx,height_px HeightPx from ged.brand_asset where tenant_id=@tenant and status='ACTIVE' and reg_status='A' order by is_default desc,asset_name",new{tenant=TenantId},cancellationToken:ct));assets=rows.Select(x=>new LogoLayoutAsset(x.Id,x.BrandName,x.AssetName,$"/Administration/BrandAssets/{x.Id}/File",x.IsDefault,x.WidthPx,x.HeightPx)).ToList();}
        var layout=posted??new LogoLayoutInput(); var validations=new List<LogoLayoutValidation>();
        if(posted is null&&ready){var row=await db.QuerySingleOrDefaultAsync<LayoutRow>(new CommandDefinition("select logo_asset_id LogoAssetId,width_mm WidthMm,height_mm HeightMm,preserve_aspect_ratio PreserveAspectRatio,fit_mode FitMode,position,position_x_mm PositionXMm,position_y_mm PositionYMm,margin_top_mm MarginTopMm,margin_left_mm MarginLeftMm,apply_to_all_copies ApplyToAllCopies,enabled from ged.print_logo_selection where tenant_id=@tenant and context_key=@key and reg_status='A'",new{tenant=TenantId,key=item.Code},cancellationToken:ct));if(row is not null)layout=row.ToInput();}
        if(layout.FitMode=="FILL")validations.Add(new("HIGH","Risco de deformação","Use CONTAIN para preservar a arte original.")); if(layout.LogoAssetId is null)validations.Add(new("MEDIUM","Nenhuma logo selecionada","O modelo será impresso sem logo."));
        return new(){TemplateCode=item.Code,TemplateName=item.Name,TemplateKind=item.Kind,Dimensions=item.Dimensions,Layout=layout,Assets=assets,Validations=validations,SchemaReady=ready};
    }
    private sealed class CardRow{public string Code{get;set;}="";public string? LogoUrl{get;set;}}
    private sealed class AssetRow{public Guid Id{get;set;}public string BrandName{get;set;}="";public string AssetName{get;set;}="";public bool IsDefault{get;set;}public int? WidthPx{get;set;}public int? HeightPx{get;set;}}
    private sealed class LayoutRow{public Guid? LogoAssetId{get;set;}public decimal WidthMm{get;set;}public decimal? HeightMm{get;set;}public bool PreserveAspectRatio{get;set;}public string FitMode{get;set;}="CONTAIN";public string Position{get;set;}="TOP_LEFT";public decimal PositionXMm{get;set;}public decimal PositionYMm{get;set;}public decimal MarginTopMm{get;set;}public decimal MarginLeftMm{get;set;}public bool ApplyToAllCopies{get;set;}public bool Enabled{get;set;}public LogoLayoutInput ToInput()=>new(){LogoAssetId=LogoAssetId,WidthMm=WidthMm,HeightMm=HeightMm,PreserveAspectRatio=PreserveAspectRatio,FitMode=FitMode,Position=Position,PositionXMm=PositionXMm,PositionYMm=PositionYMm,MarginTopMm=MarginTopMm,MarginLeftMm=MarginLeftMm,ApplyToAllCopies=ApplyToAllCopies,Enabled=Enabled};}
}
