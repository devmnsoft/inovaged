using System.Security.Claims;
using InovaGed.Application.Administration;
using InovaGed.Application.Readiness;
using InovaGed.Web.Models.Administration;
using InovaGed.Web.Security;
using InovaGed.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Administracao)]
public sealed class AdministrationController : Controller
{
    private readonly IAdministrationDashboardService _service;
    private readonly IModuleReadinessService _readiness;
    private readonly IConfiguration _configuration;
    private readonly IAtlasIconRegistry _atlasIcons;
    public AdministrationController(IAdministrationDashboardService service, IModuleReadinessService readiness, IConfiguration configuration, IAtlasIconRegistry atlasIcons) { _service = service; _readiness = readiness; _configuration = configuration; _atlasIcons = atlasIcons; }
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var overview = await _service.GetOverviewAsync(CurrentTenant(), ct);
        var actions = new[]
        {
            new AdministrationActionVM("Usuários e Perfis", "Gerencie acessos, papéis e vínculos de usuários.", "bi-people", "Administration", "Users", AppPolicies.Administracao, "Governança e Segurança", true, null),
            new AdministrationActionVM("Permissões e Segurança", "Revise políticas e o catálogo de permissões do ambiente.", "bi-shield-lock", "Administration", "Security", AppPolicies.Administracao, "Governança e Segurança", true, null),
            new AdministrationActionVM("Tenants", "Acompanhe organizações, escopos e isolamento operacional.", "bi-buildings", "Administration", "Tenants", AppPolicies.Administracao, "Governança e Segurança", true, null),
            new AdministrationActionVM("Banco e Migrations", "Valide compatibilidade do schema e prontidão de dados.", "bi-database-check", "Administration", "Migrations", AppPolicies.Administracao, "Ambiente e Banco de Dados", true, null),
            new AdministrationActionVM("Saúde do Sistema", "Acompanhe dependências e serviços essenciais.", "bi-heart-pulse", "Administration", "Health", AppPolicies.Administracao, "Ambiente e Banco de Dados", true, null),
            new AdministrationActionVM("Workers e Filas", "Monitore processamento assíncrono e atividade técnica.", "bi-activity", "Administration", "Workers", AppPolicies.Administracao, "Operação Técnica", true, null),
            new AdministrationActionVM("Central de Incidentes", "Investigue erros, rotas instáveis e pendências técnicas.", "bi-exclamation-triangle", "SystemIncidents", "Index", AppPolicies.Administracao, "Operação Técnica", true, null),
            new AdministrationActionVM("Release Readiness", "Confirme evidências antes da homologação e entrega.", "bi-rocket-takeoff", "Administration", "Readiness", AppPolicies.Administracao, "Homologação e Entrega", true, null),
            new AdministrationActionVM("Continuidade", "Acompanhe backup, recuperação e portabilidade.", "bi-life-preserver", "Continuity", "Overview", AppPolicies.ContinuityView, "Homologação e Entrega", true, null),
            new AdministrationActionVM("Configurações Seguras", "Consulte parâmetros operacionais sem expor segredos.", "bi-sliders", "Administration", "Settings", AppPolicies.Administracao, "Cadastros e Configurações", true, null),
            new AdministrationActionVM("Auditoria", "Consulte eventos administrativos e trilhas de acesso.", "bi-journal-check", "Administration", "Audit", AppPolicies.Administracao, "Cadastros e Configurações", true, null)
        };
        var sections = actions.GroupBy(action => action.Category)
            .Select(group => new AdministrationSectionVM(group.Key, $"Recursos de {group.Key.ToLowerInvariant()}.", group.ToArray()))
            .ToArray();
        var health = overview.Metrics.Select(metric => new AdministrationHealthVM(metric.Title, metric.Value, metric.State.ToString(), metric.Reason ?? metric.Guidance, metric.Icon ?? "bi-info-circle")).ToArray();
        var recommendations = overview.Recommendations.Select(item => new AdministrationRecommendationVM(item.Title, item.Reason, item.Guidance, item.Severity)).ToArray();
        return View("Index", new AdministrationDashboardVM(health, sections, recommendations));
    }
    [HttpGet("/Administration/AtlasIcons")]
    public IActionResult AtlasIcons() => View(_atlasIcons.GetAll());

    public async Task<IActionResult> Security(string? search, CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Segurança e Permissões", SecurityConfigurations = await _service.GetSecurityConfigurationsAsync(CurrentTenant(), ct), PermissionCatalog = await _service.GetPermissionCatalogAsync(search, ct) });
    public Task<IActionResult> Roles(string? search, CancellationToken ct) => Security(search, ct);
    public Task<IActionResult> Permissions(string? search, CancellationToken ct) => Security(search, ct);
    public async Task<IActionResult> Identities(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Identidades e CPF", IdentitySummary = await _service.GetIdentityMigrationSummaryAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Users(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Usuários e Autoridades", Items = await _service.GetUsersAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Audit(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Auditoria e Acessos", Items = await _service.GetAuditEventsAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Tenants(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Tenants", Items = await _service.GetTenantsAsync(CurrentTenant(), AppMenuPolicy.IsFullAdmin(User), ct) });
    public async Task<IActionResult> Workers(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Workers e Filas", Items = await _service.GetWorkersAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Health(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Saúde do Sistema", Items = await _service.GetHealthAsync(ct) });
    public async Task<IActionResult> Settings(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Configurações Seguras", Items = await _service.GetSafeConfigurationsAsync(ct) });
    public async Task<IActionResult> Migrations(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Migrações e Compatibilidade", Items = await _service.GetMigrationsAsync(ct) });
    public async Task<IActionResult> Compliance(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Conformidade e LGPD", Compliance = await _service.GetComplianceAsync(CurrentTenant(), ct) });
    [HttpGet("/Administration/Readiness")]
    public async Task<IActionResult> Readiness(CancellationToken ct)
    {
        var release = new ReleaseReadinessVM(
            _configuration["Deployment:Version"] ?? "04.1.24",
            _configuration["Deployment:Commit"] ?? "não informado",
            _configuration["Deployment:DeploymentId"] ?? "não informado",
            _configuration["Deployment:PreviousReleaseId"] ?? "não disponível",
            _configuration["Deployment:SchemaVersion"] ?? "não informado",
            DateTimeOffset.TryParse(_configuration["Deployment:DeployedAtUtc"], out var deployedAt) ? deployedAt : null,
            !string.IsNullOrWhiteSpace(_configuration["Deployment:PreviousReleaseId"]));
        return View(new EnvironmentReadinessVM(await LoadReadinessAsync(ct), DateTimeOffset.UtcNow, release));
    }
    [HttpGet("/Administration/Readiness/Export")]
    public async Task<IActionResult> ExportReadiness(CancellationToken ct)
    {
        var modules = await LoadReadinessAsync(ct);
        return Json(new { applicationVersion = "04.1.20", databaseVersion = modules[0].Status, moduleStatuses = modules.Select(item => new { item.ModuleCode, item.Status, item.Available }), missingTables = modules.SelectMany(item => item.MissingObjects).Distinct(), missingColumns = Array.Empty<string>(), migrationStatuses = Array.Empty<object>(), workerStatuses = modules.Where(item => item.ModuleCode == "Workers").Select(item => item.Status), checkedAtUtc = DateTimeOffset.UtcNow, correlationId = HttpContext.TraceIdentifier });
    }
    private async Task<IReadOnlyList<ModuleReadinessResult>> LoadReadinessAsync(CancellationToken ct)
    {
        string[] modules = ["Banco de Dados", "Identidade e Acessos", "GED", "Storage", "Preview", "OCR", "Classificação", "Temporalidade", "Guarda Física", "Empréstimos", "Protocolos", "Assinaturas", "Continuity", "Workers", "Auditoria"];
        return await Task.WhenAll(modules.Select(item => _readiness.GetAsync(item, ct)));
    }
    private Guid? CurrentTenant() => Guid.TryParse(User.FindFirst("tenant_id")?.Value ?? User.FindFirst("tenant")?.Value, out var id) ? id : null;
}
