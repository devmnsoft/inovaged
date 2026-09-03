using System.Security.Claims;
using InovaGed.Application.Administration;
using InovaGed.Application.Readiness;
using InovaGed.Web.Models.Administration;
using InovaGed.Web.Security;
using InovaGed.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Administracao)]
public sealed class AdministrationController : Controller
{
    private readonly IAdministrationDashboardService _service;
    private readonly IModuleReadinessService _readiness;
    private readonly IConfiguration _configuration;
    private readonly IAtlasIconRegistry _atlasIcons;
    private readonly IConsistencyAuditService _consistency;
    public AdministrationController(IAdministrationDashboardService service, IModuleReadinessService readiness, IConfiguration configuration, IAtlasIconRegistry atlasIcons, IConsistencyAuditService consistency) { _service = service; _readiness = readiness; _configuration = configuration; _atlasIcons = atlasIcons; _consistency = consistency; }
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var overview = await _service.GetOverviewAsync(CurrentTenant(), ct);
        var actions = new[]
        {
            new AdministrationActionVM("GED Inteligente", "Análise documental, classificação sugerida, temporalidade assistida e busca inteligente.", "bi-stars", "SmartGed", "Index", AppPolicies.Administracao, "GED e Operação", true, null),
            new AdministrationActionVM("Assistente Documental", "Perguntas rastreáveis sobre acervo, documentos, etiquetas e temporalidade.", "bi-chat-dots", "SmartAssistant", "Index", AppPolicies.Administracao, "GED e Operação", true, null),
            new AdministrationActionVM("Workflow Inteligente", "Tarefas, SLA e pendências documentais geradas a partir da inteligência operacional.", "bi-diagram-3", "SmartWorkflow", "Index", AppPolicies.Administracao, "GED e Operação", true, null),
            new AdministrationActionVM("Governança Documental", "Auditoria, LGPD, riscos, evidências, alertas e relatórios executivos.", "shield-check", "Governance", "Index", AppPolicies.Administracao, "Segurança e Acesso", true, null),
            new AdministrationActionVM("Central de Etiquetas", "Configure modelos e inicie impressões com rastreabilidade.", "bi-tags", "Labels", "Index", AppPolicies.Administracao, "Etiquetas e Impressão", true, null),
            new AdministrationActionVM("Histórico de Impressões", "Audite emissões, reimpressões, usuários e origens.", "bi-clock-history", "Labels", "History", AppPolicies.Administracao, "Etiquetas e Impressão", true, null),
            new AdministrationActionVM("Logos e Marcas", "Gerencie os ativos de marca usados nas etiquetas.", "bi-image", "BrandAssets", "Index", AppPolicies.Administracao, "Etiquetas e Impressão", true, null),
            new AdministrationActionVM("Identidade Visual", "Configure perfis de branding para impressão.", "bi-palette", "PrintBranding", "Index", AppPolicies.Administracao, "Etiquetas e Impressão", true, null),
            new AdministrationActionVM("Calibração", "Ajuste margens, escala e deslocamento físico.", "bi-sliders", "Labels", "Calibration", AppPolicies.Administracao, "Etiquetas e Impressão", true, null),
            new AdministrationActionVM("Documentos", "Acesse a operação documental do GED.", "bi-file-earmark-text", "Ged", "Index", AppPolicies.Administracao, "GED e Operação", true, null),
            new AdministrationActionVM("Plano de Classificação", "Organize classes e temporalidade documental.", "bi-diagram-3", "ClassificationPlan", "Index", AppPolicies.Administracao, "GED e Operação", true, null),
            new AdministrationActionVM("Acervo Físico", "Gerencie caixas, localizações e movimentações.", "bi-box-seam", "Physical", "Dashboard", AppPolicies.Administracao, "GED e Operação", true, null),
            new AdministrationActionVM("Usuários e Perfis", "Gerencie acessos, papéis e vínculos de usuários.", "bi-people", "Administration", "Users", AppPolicies.Administracao, "Segurança e Acesso", true, null),
            new AdministrationActionVM("Permissões e Segurança", "Revise políticas e o catálogo de permissões do ambiente.", "bi-shield-lock", "Administration", "Security", AppPolicies.Administracao, "Segurança e Acesso", true, null),
            new AdministrationActionVM("Tenants", "Acompanhe organizações, escopos e isolamento operacional.", "bi-buildings", "Administration", "Tenants", AppPolicies.Administracao, "Segurança e Acesso", true, null),
            new AdministrationActionVM("Banco e Migrations", "Valide compatibilidade do schema e prontidão de dados.", "bi-database-check", "Administration", "Migrations", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Saúde do Sistema", "Acompanhe dependências e serviços essenciais.", "bi-heart-pulse", "Administration", "Health", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Prontidão do Banco", "Valide conectividade, migrations e requisitos antes da operação.", "bi-database-check", "DatabaseReadiness", "Index", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("SchemaHealth", "Inspecione a integridade do schema e incompatibilidades conhecidas.", "bi-activity", "SchemaHealth", "Index", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Workers e Filas", "Monitore processamento assíncrono e atividade técnica.", "bi-activity", "Administration", "Workers", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Central de Incidentes", "Investigue erros, rotas instáveis e pendências técnicas.", "bi-exclamation-triangle", "SystemIncidents", "Index", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Qualidade Técnica", "Build, rotas, Razor, Dapper, segurança, migrations, tenant isolation e performance.", "shield-check", "Administration", "Quality", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Relatório de Inconsistências", "Detecte referências ausentes com isolamento por tenant e sem correções automáticas.", "list-check", "Administration", "Consistency", AppPolicies.Administracao, "Sistema e Qualidade", true, null),
            new AdministrationActionVM("Release Readiness", "Confirme evidências antes da homologação e entrega.", "bi-rocket-takeoff", "Administration", "Readiness", AppPolicies.Administracao, "Homologação e Entrega", true, null),
            new AdministrationActionVM("UAT e Go-Live", "Acompanhe planos de aceite, evidências e critérios de entrada em produção.", "bi-clipboard-check", "UatReadiness", "Index", AppPolicies.Administracao, "Homologação e Entrega", true, null),
            new AdministrationActionVM("Continuidade", "Acompanhe backup, recuperação e portabilidade.", "bi-life-preserver", "Continuity", "Overview", AppPolicies.ContinuityView, "Homologação e Entrega", true, null),
            new AdministrationActionVM("Configurações Seguras", "Consulte parâmetros operacionais sem expor segredos.", "bi-sliders", "Administration", "Settings", AppPolicies.Administracao, "Configurações", true, null),
            new AdministrationActionVM("Auditoria", "Consulte eventos administrativos e trilhas de acesso.", "bi-journal-check", "Administration", "Audit", AppPolicies.Administracao, "Configurações", true, null),
            new AdministrationActionVM("Design System", "Padrões visuais, componentes e identidade da interface do InovaGED.", "bi-layout-text-window-reverse", "Administration", "DesignSystem", AppPolicies.Administracao, "Configurações", true, null)
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

    [HttpGet("/Administration/DesignSystem")]
    public IActionResult DesignSystem() => View();

    [HttpGet("/Administration/Quality")]
    public async Task<IActionResult> Quality(CancellationToken ct)
    {
        var reportPath = Path.GetFullPath(Path.Combine(_configuration["QualityGate:ReportPath"] ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "quality-gate", "quality-gate-2-report.json")));
        if (!System.IO.File.Exists(reportPath)) return View(new QualityCenterVM(null, "Não executado", [], "Execute o Quality Gate 2.0 para gerar a evidência automática."));
        try
        {
            await using var stream = System.IO.File.OpenRead(reportPath);
            using var report = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = report.RootElement;
            var checks = root.TryGetProperty("Checks", out var items)
                ? items.EnumerateArray().Select(item => new QualityCenterCheckVM(item.GetProperty("Check").GetString() ?? "Check", item.GetProperty("Status").ToString(), item.GetProperty("Message").GetString() ?? "", item.TryGetProperty("Action", out var action) ? action.GetString() : null)).ToArray()
                : [];
            return View(new QualityCenterVM(root.TryGetProperty("GeneratedAtUtc", out var generated) ? generated.GetDateTimeOffset() : null, root.TryGetProperty("Status", out var status) ? status.ToString() : "Desconhecido", checks, null));
        }
        catch (JsonException) { return View(new QualityCenterVM(null, "Relatório inválido", [], "O relatório não é JSON válido; execute novamente o Quality Gate.")); }
        catch (IOException) { return View(new QualityCenterVM(null, "Indisponível", [], "O relatório está temporariamente indisponível para leitura.")); }
    }

    public async Task<IActionResult> Security(string? search, CancellationToken ct) => View("Security", new AdministrationPageVm { Section = "Segurança e Permissões", SecurityConfigurations = await _service.GetSecurityConfigurationsAsync(CurrentTenant(), ct), PermissionCatalog = await _service.GetPermissionCatalogAsync(search, ct) });
    public Task<IActionResult> Roles(string? search, CancellationToken ct) => Security(search, ct);
    public Task<IActionResult> Permissions(string? search, CancellationToken ct) => Security(search, ct);
    public async Task<IActionResult> Identities(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Identidades e CPF", IdentitySummary = await _service.GetIdentityMigrationSummaryAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Users(CancellationToken ct) => View("Users", new AdministrationPageVm { Section = "Usuários e Autoridades", Items = await _service.GetUsersAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Audit(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Auditoria e Acessos", Items = await _service.GetAuditEventsAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Tenants(CancellationToken ct) => View("Tenants", new AdministrationPageVm { Section = "Tenants", Items = await _service.GetTenantsAsync(CurrentTenant(), AppMenuPolicy.IsFullAdmin(User), ct) });
    public async Task<IActionResult> Workers(CancellationToken ct) => View("Workers", new AdministrationPageVm { Section = "Workers e Filas", Items = await _service.GetWorkersAsync(CurrentTenant(), ct) });
    public async Task<IActionResult> Health(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Saúde do Sistema", Items = await _service.GetHealthAsync(ct) });
    public async Task<IActionResult> Settings(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Configurações Seguras", Items = await _service.GetSafeConfigurationsAsync(ct) });
    public async Task<IActionResult> Migrations(CancellationToken ct) => View("Migrations", new AdministrationPageVm { Section = "Migrações e Compatibilidade", Items = await _service.GetMigrationsAsync(ct) });
    public async Task<IActionResult> Compliance(CancellationToken ct) => View("Section", new AdministrationPageVm { Section = "Conformidade e LGPD", Compliance = await _service.GetComplianceAsync(CurrentTenant(), ct) });
    [HttpGet("/Administration/Consistency")]
    public async Task<IActionResult> Consistency(CancellationToken ct)
    {
        var tenantId = CurrentTenant();
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty) return Forbid();
        return View(await _consistency.CheckAsync(tenantId.Value, ct));
    }
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
