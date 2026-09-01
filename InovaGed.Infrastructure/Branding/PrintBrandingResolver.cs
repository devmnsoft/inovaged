using Dapper;
using InovaGed.Application.Branding;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Branding;

public sealed class PrintBrandingResolver(IDbConnectionFactory factory) : IPrintBrandingResolver, IPrintBrandingProfileService, IPrintBrandingBindingService
{
    public Task<ResolvedPrintBranding> GetAsync(Guid tenantId, Guid profileId, CancellationToken ct) => ResolveAsync(tenantId, "", "", profileId, null, ct);
    public Task<ResolvedPrintBranding> ResolveBindingAsync(Guid tenantId, string context, string bindingKey, CancellationToken ct) => ResolveAsync(tenantId, context, bindingKey, null, null, ct);

    public async Task<ResolvedPrintBranding> ResolveAsync(Guid tenantId, string context, string bindingKey, Guid? selectedProfileId, Guid? selectedLogoAssetId, CancellationToken ct)
    {
        using var db = factory.CreateConnection(); db.Open();
        if (!await db.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.print_branding_profile') is not null", cancellationToken: ct))) return new();
        if (!selectedProfileId.HasValue && selectedLogoAssetId.HasValue)
        {
            var valid = await db.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.brand_asset where id=@id and tenant_id=@tenant and status='ACTIVE' and reg_status='A')", new { id=selectedLogoAssetId, tenant=tenantId }, cancellationToken:ct));
            if (valid) return new ResolvedPrintBranding { HasBranding=true, Message="Logo selecionada manualmente.", PrimaryLogoAssetId=selectedLogoAssetId };
        }
        const string sql = """
            select p.id, p.profile_name, p.primary_logo_asset_id, p.secondary_logo_asset_id,
                   p.header_title, p.header_subtitle, p.header_extra_line, p.footer_text, p.footer_extra_line,
                   p.primary_logo_width_mm, p.secondary_logo_width_mm, p.show_generated_at, p.show_page_number
              from ged.print_branding_profile p
             where p.tenant_id=@tenant and p.status='ACTIVE' and p.reg_status='A'
               and (p.id=@selected or (@selected is null and (p.id=(select b.profile_id from ged.print_branding_binding b where b.tenant_id=@tenant and b.binding_context=@context and b.binding_key=@key and b.enabled and b.reg_status='A' limit 1) or p.is_default)))
             order by (p.id=@selected) desc, (p.id=(select b.profile_id from ged.print_branding_binding b where b.tenant_id=@tenant and b.binding_context=@context and b.binding_key=@key and b.enabled and b.reg_status='A' limit 1)) desc, p.is_default desc limit 1
            """;
        var row = await db.QuerySingleOrDefaultAsync<DbRow>(new CommandDefinition(sql,new{tenant=tenantId,selected=selectedProfileId,context,key=bindingKey},cancellationToken:ct));
        return row is null ? new() : new ResolvedPrintBranding { HasBranding=true, Message="Identidade visual resolvida.", ProfileId=row.Id,ProfileName=row.ProfileName,PrimaryLogoAssetId=row.PrimaryLogoAssetId,SecondaryLogoAssetId=row.SecondaryLogoAssetId,HeaderTitle=row.HeaderTitle,HeaderSubtitle=row.HeaderSubtitle,HeaderExtraLine=row.HeaderExtraLine,FooterText=row.FooterText,FooterExtraLine=row.FooterExtraLine,PrimaryLogoWidthMm=row.PrimaryLogoWidthMm,SecondaryLogoWidthMm=row.SecondaryLogoWidthMm,ShowGeneratedAt=row.ShowGeneratedAt,ShowPageNumber=row.ShowPageNumber };
    }
    private sealed class DbRow { public Guid Id {get;set;} public string ProfileName {get;set;}=""; public Guid? PrimaryLogoAssetId {get;set;} public Guid? SecondaryLogoAssetId {get;set;} public string? HeaderTitle {get;set;} public string? HeaderSubtitle {get;set;} public string? HeaderExtraLine {get;set;} public string? FooterText {get;set;} public string? FooterExtraLine {get;set;} public decimal PrimaryLogoWidthMm {get;set;} public decimal SecondaryLogoWidthMm {get;set;} public bool ShowGeneratedAt {get;set;} public bool ShowPageNumber {get;set;} }
}
