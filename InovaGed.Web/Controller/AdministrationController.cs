using System.Security.Claims;
using InovaGed.Application.Administration;
using InovaGed.Web.Models.Administration;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Administracao)]
public sealed class AdministrationController : Controller
{
    private readonly IAdministrationDashboardService _service;
    public AdministrationController(IAdministrationDashboardService service) => _service = service;
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var overview = await _service.GetOverviewAsync(CurrentTenant(), ct);
        var actions = new[]
        {
            new AdministrationActionVM("Usuários", "Gerencie identidades, vínculos e acessos autorizados.", "bi-people", "Administration", "Users", AppPolicies.Administracao, "Pessoas e acessos", true, null),
            new AdministrationActionVM("Perfis e permissões", "Revise políticas e o catálogo de permissões do ambiente.", "bi-shield-check", "Administration", "Security", AppPolicies.Administracao, "Perfis e permissões", true, null),
            new AdministrationActionVM("Configurações seguras", "Consulte parâmetros operacionais sem expor segredos.", "bi-sliders", "Administration", "Settings", AppPolicies.Administracao, "Parâmetros", true, null),
            new AdministrationActionVM("Saúde do sistema", "Acompanhe dependências, workers e serviços essenciais.", "bi-heart-pulse", "Administration", "Health", AppPolicies.Administracao, "Infraestrutura", true, null),
            new AdministrationActionVM("Continuidade", "Acompanhe backup, recuperação e portabilidade.", "bi-life-preserver", "Continuity", "Overview", AppPolicies.ContinuityView, "Continuidade", true, null),
            new AdministrationActionVM("Auditoria", "Consulte eventos administrativos e trilhas de acesso.", "bi-journal-check", "Administration", "Audit", AppPolicies.Administracao, "Auditoria", true, null)
        };
        var sections = actions.GroupBy(action => action.Category)
            .Select(group => new AdministrationSectionVM(group.Key, $"Recursos de {group.Key.ToLowerInvariant()}.", group.ToArray()))
            .ToArray();
        var health = overview.Metrics.Select(metric => new AdministrationHealthVM(metric.Title, metric.Value, metric.State.ToString(), metric.Reason ?? metric.Guidance, metric.Icon ?? "bi-info-circle")).ToArray();
        var recommendations = overview.Recommendations.Select(item => new AdministrationRecommendationVM(item.Title, item.Reason, item.Guidance, item.Severity)).ToArray();
        return View("Index", new AdministrationDashboardVM(health, sections, recommendations));
    }
    public async Task<IActionResult> Security(string? search, CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Segurança e Permissões", SecurityConfigurations = await _service.GetSecurityConfigurationsAsync(CurrentTenant(), ct), PermissionCatalog = await _service.GetPermissionCatalogAsync(search, ct) });
    public async Task<IActionResult> Identities(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Identidades e CPF", IdentitySummary = await _service.GetIdentityMigrationSummaryAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Users(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Usuários e Autoridades", Items = await _service.GetUsersAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Audit(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Auditoria e Acessos", Items = await _service.GetAuditEventsAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Tenants(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Tenants", Items = await _service.GetTenantsAsync(CurrentTenant(), AppMenuPolicy.IsFullAdmin(User), ct) });
    public async Task<IActionResult> Workers(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Workers e Filas", Items = await _service.GetWorkersAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Health(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Saúde do Sistema", Items = await _service.GetHealthAsync(ct) });
    public async Task<IActionResult> Settings(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Configurações Seguras", Items = await _service.GetSafeConfigurationsAsync(ct) });
    public async Task<IActionResult> Migrations(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Migrações e Compatibilidade", Items = await _service.GetMigrationsAsync(ct) });
    public async Task<IActionResult> Compliance(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Conformidade e LGPD", Compliance = await _service.GetComplianceAsync(CurrentTenant(), ct) });
    private Guid? CurrentTenant() => Guid.TryParse(User.FindFirst("tenant_id")?.Value ?? User.FindFirst("tenant")?.Value, out var id) ? id : null;
}
