using System.Security.Claims;
using InovaGed.Web.Models.AppShell;
using InovaGed.Web.Security;

namespace InovaGed.Web.Services;

public interface IUserShellContextService
{
    AppShellVM Create(ClaimsPrincipal user, string? pageTitle, string? pageSubtitle);
}

public sealed class UserShellContextService : IUserShellContextService
{
    public AppShellVM Create(ClaimsPrincipal user, string? pageTitle, string? pageSubtitle)
    {
        var roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        var role = SelectPrimaryRole(roles);
        var sector = FirstClaim(user, "sector", "setor", "lotacao", "sector_name");
        var displayName = user.Identity?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Usuário";

        var shellUser = new AppUserShellVM(
            displayName,
            Initials(displayName),
            role,
            RoleLabel(role),
            sector,
            RequiresSector(role) && string.IsNullOrWhiteSpace(sector));

        var primaryAction = AppMenuPolicy.IsFullAdmin(user)
            ? new AppPrimaryActionVM("Novo Documento", "Ged", "Create", "document-add")
            : null;

        var moduleLabel = ModuleLabel(pageTitle);
        var title = string.IsNullOrWhiteSpace(pageTitle) ? "InovaGED" : pageTitle;
        var moduleCode = moduleLabel.ToLowerInvariant().Replace(' ', '-');

        return new AppShellVM(
            new AppBrandVM("InovaGED", "Workspace Documental", "GedDashboard", "Index"),
            new AppEnvironmentVM(sector ?? "Workspace institucional", "Ambiente seguro"),
            new AppPageContextVM(
                moduleCode,
                moduleLabel,
                title,
                pageSubtitle,
                PageIcon(moduleLabel),
                new[] { new AppBreadcrumbItemVM(moduleLabel), new AppBreadcrumbItemVM(title) },
                Array.Empty<AppContextStatusVM>()),
            shellUser,
            BuildMenu(user),
            BuildQuickActions(user),
            Array.Empty<AppUtilityActionVM>(),
            primaryAction);
    }

    private static IReadOnlyList<AppMenuSectionVM> BuildMenu(ClaimsPrincipal user)
    {
        if (AppMenuPolicy.IsFullAdmin(user))
        {
            return new[]
            {
                Section("Visão Geral",
                    Item("Dashboard", "GedDashboard", "Index", "dashboard"),
                    Item("Central Operacional", "Operations", "Index", "workspace"),
                    Item("Qualidade Documental", "DocumentQuality", "Index", "check"),
                    Item("Alertas e Tendências", "HospitalTrends", "Index", "activity")),
                Section("Gestão Documental",
                    Item("GED / Explorer", "Ged", "Index", "folder-open"),
                    Item("Busca Hospitalar", "HospitalDocuments", "Index", "document-search"),
                    Item("Busca Inteligente", "SmartSearch", "Index", "smart-search"),
                    Item("Inteligência Hospitalar", "HospitalIntelligence", "Index", "activity"),
                    Item("Uploads", "GedUploads", "Index", "upload-cloud"),
                    Item("Central OCR", "Ocr", "Index", "ocr"),
                    Item("Agendamento OCR", "Ocr", "AutoSchedule", "recent"),
                    Item("Classificação", "GedClassification", "Queue", "classification")),
                Section("Instrumentos Arquivísticos",
                    Item("Plano de Classificação Documental - PCD", "ClassificationPlan", "Index", "classification"),
                    Item("Tabela de Temporalidade - TTD", "Retention", "Index", "recent"),
                    Item("Procedimentos Operacionais - POP", "Instruments", "Pop", "document-version"),
                    Item("Versões e Publicações", "InstrumentVersions", "Index", "document-history", new Dictionary<string, string> { ["type"] = "PCD" }),
                    Item("Casos de Temporalidade", "RetentionCase", "Index", "saved-view"),
                    Item("Destinação Documental", "RetentionDestination", "Index", "document-move")),
                Section("Arquivo Físico",
                    Item("Lotes", "Batches", "Index", "cards"),
                    Item("Localizações", "Physical", "Locations", "folder"),
                    Item("Caixas", "Physical", "Boxes", "documents"),
                    Item("Etiquetas", "Labels", "Boxes", "metadata"),
                    Item("Empréstimos", "Loans", "Index", "loan"),
                    Item("Devoluções e Atrasos", "Loans", "Overdue", "recent")),
                Section("Atendimento",
                    Item("Protocolos", "Protocolo", "Index", "protocol"),
                    Item("Solicitar Protocolo", "ProtocolRequests", "New", "protocol-add"),
                    Item("Minhas Solicitações", "ProtocolRequests", "My", "list"),
                    Item("Fila de Protocolos", "Protocols", "WorkQueue", "documents"),
                    Item("Workflows", "Workflow", "Index", "workspace")),
                Section("Governança",
                    Item("Assinaturas", "Signature", "Index", "signature"),
                    Item("Auditoria", "Audit", "Index", "audit"),
                    Item("Relatórios", "Reports", "PcdFull", "report"),
                    Item("Fontes de Autoridade", "AuthoritySources", "Index", "database"),
                    Item("Continuidade e Portabilidade", "Continuity", "Overview", "health")),
                Section("Administração",
                    Item("Administração", "Administration", "Index", "settings"),
                    Item("Parâmetros", "Parameters", "Index", "settings"),
                    Item("Usuários", "Users", "Index", "users"),
                    Item("Perfis e Permissões", "Security", "Roles", "roles"),
                    Item("Schema do Banco", "SchemaHealth", "Index", "database"),
                    Item("PACS", "Pacs", "Index", "activity"),
                    Item("Saúde do Sistema", "SystemHealth", "Index", "health"))
            };
        }

        if (AppMenuPolicy.IsAdministradorOphir(user))
            return new[] { Section("Setor", Item("Documentos Hospitalares", "HospitalDocuments", "Index", "document-search"), Item("Pedidos do Setor", "Loans", "Index", "loan"), Item("Fila de Protocolos", "Protocols", "WorkQueue", "documents"), Item("Usuários do meu setor", "Users", "Sector", "users")) };

        if (AppMenuPolicy.IsArquivistaOphir(user))
            return new[] { Section("Solicitações", Item("Documentos Hospitalares", "HospitalDocuments", "Index", "document-search"), Item("Novo Pedido de Documento", "Loans", "New", "document-add"), Item("Meus Pedidos", "Loans", "Index", "documents"), Item("Meus Protocolos", "ProtocolRequests", "My", "list")) };

        if (AppMenuPolicy.IsHospitalUser(user))
            return new[] { Section("Consulta", Item("Documentos Hospitalares", "HospitalDocuments", "Index", "document-search")) };

        return new[] { Section("Consulta", Item("Buscar Prontuários e Documentos", "HospitalDocuments", "Index", "document-search"), Item("Busca Inteligente", "SmartSearch", "Index", "smart-search")) };
    }

    private static IReadOnlyList<AppQuickActionVM> BuildQuickActions(ClaimsPrincipal user)
    {
        if (AppMenuPolicy.IsFullAdmin(user))
        {
            return
            [
                new("upload", "Enviar Arquivos", "Abrir a central de uploads.", "GedUploads", "Index", "upload-cloud", true),
                new("protocol", "Novo Protocolo", "Registrar um novo atendimento.", "Protocolo", "Novo", "protocol-add", false),
                new("loan", "Novo Empréstimo", "Registrar a movimentação autorizada.", "Loans", "New", "loan", false)
            ];
        }

        if (AppMenuPolicy.IsArquivistaOphir(user))
            return [new("loan", "Novo Empréstimo", "Solicitar um documento autorizado.", "Loans", "New", "loan", true)];

        return [];
    }

    private static string ModuleLabel(string? pageTitle)
    {
        var title = pageTitle ?? string.Empty;
        if (title.Contains("Protocolo", StringComparison.OrdinalIgnoreCase)) return "Atendimento";
        if (title.Contains("Empréstimo", StringComparison.OrdinalIgnoreCase)) return "Arquivo Físico";
        if (title.Contains("Usuário", StringComparison.OrdinalIgnoreCase) || title.Contains("Admin", StringComparison.OrdinalIgnoreCase)) return "Administração";
        if (title.Contains("Documento", StringComparison.OrdinalIgnoreCase) || title.Contains("GED", StringComparison.OrdinalIgnoreCase) || title.Contains("Busca", StringComparison.OrdinalIgnoreCase)) return "Gestão Documental";
        return "Visão Geral";
    }

    private static string PageIcon(string moduleLabel) => moduleLabel switch
    {
        "Atendimento" => "protocol",
        "Arquivo Físico" => "loan",
        "Administração" => "settings",
        "Gestão Documental" => "documents",
        _ => "dashboard"
    };

    private static AppMenuSectionVM Section(string label, params AppMenuItemVM[] items) =>
        new(Slug(label), label, items.Select((item, index) => item with { Section = Slug(label), Order = index }).ToArray());

    private static AppMenuItemVM Item(string label, string controller, string action, string icon, IDictionary<string, string>? routeValues = null) =>
        new(
            $"{controller}.{action}".ToLowerInvariant(), string.Empty,
            label,
            $"Abrir {label.ToLowerInvariant()}.",
            icon,
            controller, action, routeValues ?? new Dictionary<string, string>(),
            null, null, 0, true,
            new AppMenuRouteRuleVM(controller, action, routeValues),
            label.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Slug(string value) => string.Concat(value.Normalize(System.Text.NormalizationForm.FormD)
        .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark))
        .ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
    private static string? FirstClaim(ClaimsPrincipal user, params string[] names) => names.Select(name => user.FindFirst(name)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static bool RequiresSector(string role) => role.Equals(AppRoles.AdministradorOphir, StringComparison.OrdinalIgnoreCase) || role.Equals(AppRoles.ArquivistaOphir, StringComparison.OrdinalIgnoreCase);
    private static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
    private static string SelectPrimaryRole(IEnumerable<string> roles) => new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir, AppRoles.Hospital }.FirstOrDefault(expected => roles.Any(role => role.Equals(expected, StringComparison.OrdinalIgnoreCase))) ?? roles.FirstOrDefault() ?? "SEM PERFIL";
    private static string RoleLabel(string role) => role.ToUpperInvariant() switch { AppRoles.Admin or AppRoles.Administrador => "Administrador do Sistema", AppRoles.AdministradorOphir => "Administrador Ophir", AppRoles.ArquivistaOphir => "Arquivista Ophir", AppRoles.Hospital => "Usuário Hospitalar", _ => "Perfil de consulta" };
}
