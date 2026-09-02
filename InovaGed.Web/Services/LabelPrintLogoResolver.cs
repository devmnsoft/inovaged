using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Branding;
using InovaGed.Web.Models.Branding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
namespace InovaGed.Web.Services;
public interface ILabelPrintLogoResolver { Task<ResolvedPrintLogo> ResolveAsync(Guid tenantId,string templateCode,string selection,Guid? selectedAssetId,decimal? widthMm,decimal? heightMm,bool preserveAspectRatio,string? fitMode,string? position,decimal offsetXmm,decimal offsetYmm,CancellationToken ct); }
public sealed class LabelPrintLogoResolver(IDbConnectionFactory factory, IBrandAssetImageService images) : ILabelPrintLogoResolver
{
 public async Task<ResolvedPrintLogo> ResolveAsync(Guid tenantId,string templateCode,string selection,Guid? selectedAssetId,decimal? widthMm,decimal? heightMm,bool preserveAspectRatio,string? fitMode,string? position,decimal offsetXmm,decimal offsetYmm,CancellationToken ct){
  selection=(selection??"TEMPLATE_DEFAULT").Trim().ToUpperInvariant(); if(selection=="NONE")return Empty(); await using var db=await factory.OpenAsync(ct);
  if(!await db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.brand_asset') is not null",cancellationToken:ct)))return Empty(); Guid? id=selection=="SELECTED"?selectedAssetId:null;
  if(id is null && selection is ("TEMPLATE_DEFAULT" or "PROFILE") && await db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.print_template_brand_binding') is not null",cancellationToken:ct))) id=await db.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("select b.logo_asset_id from ged.print_template_brand_binding b join ged.brand_asset a on a.id=b.logo_asset_id and a.tenant_id=@tenantId and a.status='ACTIVE' and a.reg_status='A' where (b.tenant_id=@tenantId or b.tenant_id is null) and b.template_code=@templateCode and b.enabled=true and b.reg_status='A' order by b.tenant_id nulls last limit 1",new{tenantId,templateCode},cancellationToken:ct));
  id??=await db.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("select id from ged.brand_asset where tenant_id=@tenantId and is_default=true and status='ACTIVE' and reg_status='A' order by created_at desc limit 1",new{tenantId},cancellationToken:ct)); if(id is null)return Empty();
  var r=await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,default_width_mm DefaultWidthMm,default_height_mm DefaultHeightMm,preserve_aspect_ratio PreserveAspectRatio,fit_mode FitMode,default_position DefaultPosition,alt_text AltText from ged.brand_asset where id=@id and tenant_id=@tenantId and status='ACTIVE' and reg_status='A'",new{id,tenantId},cancellationToken:ct)); if(r is null)return Empty();
  var fit=(fitMode??r.FitMode).ToUpperInvariant();if(fit is not("CONTAIN" or "COVER" or "FILL"))fit="CONTAIN";var pos=(position??r.DefaultPosition).ToUpperInvariant();
  var image = await images.GetImageAsync(tenantId,r.Id,ct);
  var webUrl = $"/Administration/BrandAssets/{r.Id}/File";
  const string loadError = "A logo selecionada não pôde ser carregada. Verifique o arquivo em Logos e Marcas.";
  return new(r.Id,r.BrandName,webUrl,image?.DataUri,string.IsNullOrWhiteSpace(r.AltText)?r.AssetName:r.AltText,widthMm is >0?widthMm.Value:r.DefaultWidthMm,heightMm is >0?heightMm:r.DefaultHeightMm,preserveAspectRatio,fit,pos,offsetXmm,offsetYmm,true,image is not null,image is null?loadError:null);
 }
 private static ResolvedPrintLogo Empty()=>new(null,null,null,null,"Sem logo",38,null,true,"CONTAIN","TOP_LEFT",0,0,false,false,null);
 private sealed class Row{public Guid Id{get;set;}public string BrandName{get;set;}="";public string AssetName{get;set;}="";public decimal DefaultWidthMm{get;set;}=38;public decimal? DefaultHeightMm{get;set;}public bool PreserveAspectRatio{get;set;}=true;public string FitMode{get;set;}="CONTAIN";public string DefaultPosition{get;set;}="TOP_LEFT";public string? AltText{get;set;}}
}
