namespace InovaGed.Application.Branding;

public interface IPrintBrandingProfileService
{
    Task<ResolvedPrintBranding> GetAsync(Guid tenantId, Guid profileId, CancellationToken ct);
}
