using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Branding;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy=AppPolicies.FullAdminOnly)]
[Route("Labels/LogoSelector")]
public sealed class LogoSelectorController(IDbConnectionFactory factory) : GedControllerBase(factory)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? returnUrl, CancellationToken ct)
    {
        using var db=await OpenAsync();
        var rows=(await db.QueryAsync<LogoRow>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,is_default IsDefault from ged.brand_asset where tenant_id=@tenant and status='ACTIVE' and reg_status='A' order by is_default desc,asset_name",new{tenant=TenantId},cancellationToken:ct))).AsList();
        ViewBag.ReturnUrl=Url.IsLocalUrl(returnUrl)?returnUrl:"/Labels/PrintWizard";
        return View(rows.Select(x=>new BrandAssetVm{Id=x.Id,BrandName=x.BrandName,AssetName=x.AssetName,IsDefault=x.IsDefault,Status="ACTIVE"}).ToArray());
    }

    [HttpPost("Upload"), ValidateAntiForgeryToken]
    public IActionResult Upload() => RedirectPreserveMethod("/Administration/BrandAssets/Create");

    [HttpPost("Preview"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(Guid logoAssetId, decimal widthMm=38, decimal? heightMm=null, bool preserveAspectRatio=true, string fitMode="CONTAIN", CancellationToken ct=default)
    {
        if(widthMm is <10 or >90 || heightMm is <5 or >60 || !new[]{"CONTAIN","COVER","FILL"}.Contains(fitMode)) return BadRequest();
        using var db=await OpenAsync();
        var row=await db.QuerySingleOrDefaultAsync<LogoRow>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,is_default IsDefault from ged.brand_asset where id=@id and tenant_id=@tenant and status='ACTIVE' and reg_status='A'",new{id=logoAssetId,tenant=TenantId},cancellationToken:ct));
        if(row is null)return NotFound();
        var source = $"/Administration/BrandAssets/{row.Id}/File";
        return View("~/Views/Administration/BrandAssets/Preview.cshtml",new PrintLogoViewModel{AssetId=row.Id,LogoUrl=source,PrintImageSource=source,Alt=row.AssetName,WidthMm=widthMm,HeightMm=heightMm,PreserveAspectRatio=preserveAspectRatio,FitMode=fitMode,HasLogo=true,ImageLoaded=true});
    }
    private sealed class LogoRow { public Guid Id {get;set;} public string BrandName {get;set;}=""; public string AssetName {get;set;}=""; public bool IsDefault {get;set;} }
}
