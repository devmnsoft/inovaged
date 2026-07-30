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
            ? new AppPrimaryActionVM("Novo Documento", "Ged", "Create", "bi-file-earmark-plus")
            : null;

        return new AppShellVM(
            string.IsNullOrWhiteSpace(pageTitle) ? "InovaGED" : pageTitle,
            pageSubtitle,
            shellUser,
            BuildMenu(user),
            primaryAction);
    }

    private static IReadOnlyList<AppMenuSectionVM> BuildMenu(ClaimsPrincipal user)
    {
        if (AppMenuPolicy.IsFullAdmin(user))
        {
            return new[]
            {
                Section("Visão Geral",
                    Item("Dashboard", "GedDashboard", "Index", "bi-speedometer2"),
                    Item("Central Operacional", "Operations", "Index", "bi-command"),
                    Item("Qualidade Documental", "DocumentQuality", "Index", "bi-shield-check")),
                Section("Documentos",
                    Item("GED / Explorer", "Ged", "Index", "bi-folder2-open", "GedSearch"),
                    Item("Busca Hospitalar", "HospitalDocuments", "Index", "bi-search-heart"),
                    Item("Busca Inteligente", "SmartSearch", "Index", "bi-search"),
                    Item("Uploads", "GedUploads", "Index", "bi-cloud-upload"),
                    Item("Central OCR", "Ocr", "Index", "bi-filetype-pdf"),
                    Item("Classificação", "GedClassification", "Queue", "bi-tags", "ClassificationDashboard"),
                    Item("Pastas", "Ged", "Folders", "bi-folder")),
                Section("Guarda Física",
                    Item("Localizações", "Physical", "Locations", "bi-geo-alt"),
                    Item("Caixas", "Physical", "Boxes", "bi-inbox"),
                    Item("Etiquetas", "Labels", "Boxes", "bi-upc-scan")),
                Section("Operação",
                    Item("Empréstimos", "Loans", "Index", "bi-arrow-left-right", "Solicitacoes"),
                    Item("Protocolo", "Protocolo", "Index", "bi-journal-check", "Protocols"),
                    Item("Minhas Solicitações", "ProtocolRequests", "My", "bi-list-check")),
                Section("Administração",
                    Item("Administração", "Administration", "Index", "bi-gear"),
                    Item("Parâmetros", "Parameters", "Index", "bi-sliders"),
                    Item("Usuários", "Users", "Index", "bi-people"),
                    Item("Logs", "SystemLogs", "Index", "bi-clock-history", "Audit", "AuditDashboard"),
                    Item("Continuidade e Portabilidade", "Continuity", "Overview", "bi-life-preserver"),
                    Item("SystemHealth", "SystemHealth", "Index", "bi-heart-pulse"))
            };
        }

        if (AppMenuPolicy.IsAdministradorOphir(user))
            return new[] { Section("Setor", Item("Documentos Hospitalares", "HospitalDocuments", "Index", "bi-search-heart"), Item("Pedidos do Setor", "Loans", "Index", "bi-arrow-left-right"), Item("Fila de Protocolos", "Protocols", "WorkQueue", "bi-inboxes"), Item("Usuários do meu setor", "Users", "Sector", "bi-people")) };

        if (AppMenuPolicy.IsArquivistaOphir(user))
            return new[] { Section("Solicitações", Item("Documentos Hospitalares", "HospitalDocuments", "Index", "bi-search-heart"), Item("Novo Pedido de Documento", "Loans", "New", "bi-file-earmark-plus"), Item("Meus Pedidos", "Loans", "Index", "bi-collection"), Item("Meus Protocolos", "ProtocolRequests", "My", "bi-list-check")) };

        if (AppMenuPolicy.IsHospitalUser(user))
            return new[] { Section("Consulta", Item("Documentos Hospitalares", "HospitalDocuments", "Index", "bi-search-heart")) };

        return new[] { Section("Consulta", Item("Buscar Prontuários e Documentos", "HospitalDocuments", "Index", "bi-search-heart"), Item("Busca Inteligente", "SmartSearch", "Index", "bi-search")) };
    }

    private static AppMenuSectionVM Section(string label, params AppMenuItemVM[] items) => new(label, items);
    private static AppMenuItemVM Item(string label, string controller, string action, string icon, params string[] alsoActive) => new(label, controller, action, icon, new[] { controller }.Concat(alsoActive).ToArray());
    private static string? FirstClaim(ClaimsPrincipal user, params string[] names) => names.Select(name => user.FindFirst(name)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static bool RequiresSector(string role) => role.Equals(AppRoles.AdministradorOphir, StringComparison.OrdinalIgnoreCase) || role.Equals(AppRoles.ArquivistaOphir, StringComparison.OrdinalIgnoreCase);
    private static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
    private static string SelectPrimaryRole(IEnumerable<string> roles) => new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir, AppRoles.Hospital }.FirstOrDefault(expected => roles.Any(role => role.Equals(expected, StringComparison.OrdinalIgnoreCase))) ?? roles.FirstOrDefault() ?? "SEM PERFIL";
    private static string RoleLabel(string role) => role.ToUpperInvariant() switch { AppRoles.Admin or AppRoles.Administrador => "Administrador do Sistema", AppRoles.AdministradorOphir => "Administrador Ophir", AppRoles.ArquivistaOphir => "Arquivista Ophir", AppRoles.Hospital => "Usuário Hospitalar", _ => "Perfil de consulta" };
}
