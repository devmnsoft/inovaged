namespace InovaGed.Application.Branding;

public interface IPrintBrandingBindingService
{
    Task<ResolvedPrintBranding> ResolveBindingAsync(Guid tenantId, string context, string bindingKey, CancellationToken ct);
}
