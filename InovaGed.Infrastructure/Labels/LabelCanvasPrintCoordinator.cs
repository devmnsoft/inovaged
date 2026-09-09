using Dapper;
using InovaGed.Application.Branding;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasPrintCoordinator(
    IDbConnectionFactory dbFactory,
    ILabelCanvasDesignService designs,
    ILabelCanvasValueResolver valueResolver,
    IPrintBrandingResolver brandingResolver,
    ILabelCanvasRenderService renderer) : ILabelCanvasPrintCoordinator
{
    public async Task<LabelCanvasPreparedRender> PrepareAsync(LabelCanvasPrintContext context,bool printMode=false,CancellationToken cancellationToken=default)
    {
        Validate(context);
        var design=await designs.GetPublishedAsync(context.TenantId,context.TemplateKey,context.TemplateVersion,cancellationToken)
            ??throw new KeyNotFoundException("O template Canvas não possui versão publicada.");
        if(context.ExecutionMode==LabelCanvasExecutionMode.SnapshotReplay && (context.ResolvedValues is null||context.BrandingSnapshot is null||context.CalibrationSnapshot is null))
            throw new InvalidOperationException("Snapshot replay exige valores, branding e calibração congelados.");
        var source=context.ResolvedValues??await valueResolver.ResolveAsync(context.TenantId,design.SubjectType,context.OperationalSubjectType,context.SubjectId,cancellationToken);
        if(source.Count==0)throw new KeyNotFoundException("Não foi possível localizar a origem da etiqueta.");
        var branding=context.BrandingSnapshot??await ResolveBrandingAsync(context,design,cancellationToken);
        var calibration=context.CalibrationSnapshot??await ResolveCalibrationAsync(context.TenantId,context.PrintProfileId,cancellationToken);
        var values=BuildValues(context,design,source,branding,calibration);
        var rendered=renderer.Render(design,values,printMode);
        return new(design,design.CurrentVersion,values,branding,calibration,rendered.Html,rendered.SnapshotHash,rendered.Validation);
    }

    public async Task<LabelCanvasPreparedRender> PrepareBatchAsync(IReadOnlyList<LabelCanvasPrintContext> contexts,bool printMode=false,CancellationToken cancellationToken=default)
    {
        if(contexts.Count==0)throw new ArgumentException("Informe ao menos uma origem.",nameof(contexts));
        var prepared=new List<LabelCanvasPreparedRender>(contexts.Count);
        foreach(var context in contexts)prepared.Add(await PrepareAsync(context,false,cancellationToken));
        var first=prepared[0];
        if(prepared.Any(x=>x.Design.TemplateKey!=first.Design.TemplateKey||x.TemplateVersion!=first.TemplateVersion))throw new InvalidOperationException("O lote deve usar a mesma versão publicada do template.");
        var rendered=renderer.RenderBatch(first.Design,prepared.Select(x=>x.Values).ToArray(),printMode);
        return first with{Html=rendered.Html,SnapshotHash=rendered.SnapshotHash,Validation=rendered.Validation};
    }

    private async Task<ResolvedPrintBranding> ResolveBrandingAsync(LabelCanvasPrintContext context,LabelCanvasDesignDto design,CancellationToken ct)
    {
        ResolvedPrintBranding branding=new();
        if(context.BrandingProfileId is Guid selected)branding=await brandingResolver.ResolveAsync(context.TenantId,PrintBrandingContext.LabelTemplate,design.BrandingBindingKey??design.TemplateKey,selected,null,ct);
        if(!branding.HasBranding&&design.DefaultBrandingProfileId is Guid fallback)branding=await brandingResolver.ResolveAsync(context.TenantId,PrintBrandingContext.LabelTemplate,design.BrandingBindingKey??design.TemplateKey,fallback,null,ct);
        if(!branding.HasBranding)branding=await brandingResolver.ResolveAsync(context.TenantId,PrintBrandingContext.LabelTemplate,design.BrandingBindingKey??design.TemplateKey,null,null,ct);
        if(context.SelectedLogoAssetId is not Guid logo)return branding;
        var selectedLogo=await brandingResolver.ResolveAsync(context.TenantId,PrintBrandingContext.LabelTemplate,design.BrandingBindingKey??design.TemplateKey,null,logo,ct);
        if(!selectedLogo.HasBranding)return branding;
        return CopyBranding(branding,selectedLogo.PrimaryLogoAssetId);
    }

    private async Task<LabelCanvasCalibration> ResolveCalibrationAsync(Guid tenantId,Guid? profileId,CancellationToken ct)
    {
        await using var db=await dbFactory.OpenAsync(ct);
        if(!await db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.label_print_profile') is not null",cancellationToken:ct)))return DefaultCalibration(profileId);
        const string sql="""
select id ProfileId,margin_top_mm MarginTopMm,margin_left_mm MarginLeftMm,offset_x_mm OffsetXMm,offset_y_mm OffsetYMm,
 scale_percent ScalePercent,label_gap_x_mm GapXMm,label_gap_y_mm GapYMm
from ged.label_print_profile where tenant_id=@tenantId and reg_status='A' and ((@profileId is not null and id=@profileId) or (@profileId is null and is_default))
order by is_default desc limit 1
""";
        var calibration=await db.QuerySingleOrDefaultAsync<LabelCanvasCalibration>(new CommandDefinition(sql,new{tenantId,profileId},cancellationToken:ct));
        if(calibration is null&&profileId.HasValue)throw new InvalidOperationException("Perfil de impressão inválido.");
        return calibration??DefaultCalibration(null);
    }

    private static Dictionary<string,object?> BuildValues(LabelCanvasPrintContext context,LabelCanvasDesignDto design,IReadOnlyDictionary<string,object?> source,ResolvedPrintBranding branding,LabelCanvasCalibration calibration)
    {
        var values=source.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        var demo=context.ExecutionMode is LabelCanvasExecutionMode.Demo or LabelCanvasExecutionMode.Preview;
        values["clientName"]=branding.ClientName??design.ClientNameFallback??(demo?"Cliente de demonstração":null);
        values["contractName"]=branding.ContractName??design.ContractNameFallback??(demo?"Contrato de demonstração":null);
        values["organizationName"]=branding.OrganizationName??design.OrganizationNameFallback??(demo?"Unidade documental":null);
        values["headerTitle"]=branding.HeaderTitle??design.HeaderTitleFallback;values["headerSubtitle"]=branding.HeaderSubtitle??design.HeaderSubtitleFallback;
        values["headerExtraLine"]=branding.HeaderExtraLine;values["footerText"]=branding.FooterText;values["footerExtraLine"]=branding.FooterExtraLine;
        values["primaryLogo"]=branding.PrimaryLogoAssetId is Guid primary?$"/Administration/BrandAssets/{primary}/File":null;
        values["secondaryLogo"]=branding.SecondaryLogoAssetId is Guid secondary?$"/Administration/BrandAssets/{secondary}/File":null;
        values["printedBy"]=context.PrintedBy;values["traceCode"]=context.RegisteredTraceCode??values.GetValueOrDefault("traceCode");
        var frozenQr=values.GetValueOrDefault("qrPayload")?.ToString();
        var relative=context.RegisteredTraceUrl??SafeOriginUrl(context.OperationalSubjectType,context.SubjectId);
        values["qrPayload"]=context.ExecutionMode==LabelCanvasExecutionMode.SnapshotReplay&&!string.IsNullOrWhiteSpace(frozenQr)?frozenQr:CombineUrl(context.AbsoluteBaseUrl,relative);values["__copies"]=Math.Clamp(context.Copies,1,100);
        values["__marginTopMm"]=calibration.MarginTopMm;values["__marginLeftMm"]=calibration.MarginLeftMm;values["__offsetXmm"]=calibration.OffsetXMm;values["__offsetYmm"]=calibration.OffsetYMm;values["__scalePercent"]=calibration.ScalePercent;values["__gapXmm"]=calibration.GapXMm;values["__gapYmm"]=calibration.GapYMm;
        return values;
    }

    internal static string SafeOriginUrl(string operationalSubjectType,Guid subjectId)=>LabelCanvasSubjectTypeMapper.ToOperational(operationalSubjectType)=="BOX"?$"/Physical/Boxes/{subjectId}":$"/Ged/Details/{subjectId}";
    private static string CombineUrl(string? baseUrl,string path)=>string.IsNullOrWhiteSpace(baseUrl)?path:$"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    private static LabelCanvasCalibration DefaultCalibration(Guid? id)=>new(id,0,0,0,0,100,4,4);
    private static void Validate(LabelCanvasPrintContext context){if(context.TenantId==Guid.Empty)throw new InvalidOperationException("Tenant obrigatório.");if(context.SubjectId==Guid.Empty)throw new ArgumentException("Origem obrigatória.");ArgumentException.ThrowIfNullOrWhiteSpace(context.TemplateKey);}
    private static ResolvedPrintBranding CopyBranding(ResolvedPrintBranding source,Guid? primaryLogo)=>new(){HasBranding=source.HasBranding||primaryLogo.HasValue,Message=source.Message,ProfileId=source.ProfileId,ProfileName=source.ProfileName,ClientName=source.ClientName,ContractName=source.ContractName,OrganizationName=source.OrganizationName,PrimaryLogoAssetId=primaryLogo,SecondaryLogoAssetId=source.SecondaryLogoAssetId,HeaderTitle=source.HeaderTitle,HeaderSubtitle=source.HeaderSubtitle,HeaderExtraLine=source.HeaderExtraLine,FooterText=source.FooterText,FooterExtraLine=source.FooterExtraLine,PrimaryLogoWidthMm=source.PrimaryLogoWidthMm,SecondaryLogoWidthMm=source.SecondaryLogoWidthMm,ShowGeneratedAt=source.ShowGeneratedAt,ShowPageNumber=source.ShowPageNumber};
}
