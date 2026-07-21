using InovaGed.Application.Administration;

namespace InovaGed.Web.Models.Administration;

public sealed class AdministrationPageVm
{
    public string Section { get; set; } = "Visão Geral";
    public AdministrationOverview? Overview { get; set; }
    public IReadOnlyList<TenantSecurityConfiguration> SecurityConfigurations { get; set; } = Array.Empty<TenantSecurityConfiguration>();
    public IReadOnlyList<PermissionCatalogItem> PermissionCatalog { get; set; } = Array.Empty<PermissionCatalogItem>();
    public IdentityMigrationSummary? IdentitySummary { get; set; }
    public IReadOnlyList<AdministrationListItem> Items { get; set; } = Array.Empty<AdministrationListItem>();
    public IReadOnlyList<ComplianceControlItem> Compliance { get; set; } = Array.Empty<ComplianceControlItem>();
}
