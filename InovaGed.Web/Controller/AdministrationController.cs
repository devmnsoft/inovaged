using System.Security.Claims;
using InovaGed.Application.Administration;
using InovaGed.Web.Models.Administration;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controller;

[Authorize(Policy = AppPolicies.Administracao)]
public sealed class AdministrationController : Controller
{
    private readonly IAdministrationDashboardService _service;
    public AdministrationController(IAdministrationDashboardService service) => _service = service;
    public async Task<IActionResult> Index(CancellationToken ct) => View("Index", new AdministrationPageVm { Overview = await _service.GetOverviewAsync(CurrentTenant(), ct) });
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
