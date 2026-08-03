namespace InovaGed.Web.Models.AppShell;

public sealed record AppShellVM(
    AppBrandVM Brand,
    AppEnvironmentVM Environment,
    AppPageContextVM Page,
    AppUserShellVM User,
    IReadOnlyList<AppMenuSectionVM> Menu,
    IReadOnlyList<AppQuickActionVM> QuickActions,
    IReadOnlyList<AppUtilityActionVM> UtilityActions,
    AppPrimaryActionVM? PrimaryAction);

public sealed record AppBrandVM(string Name, string Product, string HomeController, string HomeAction);
public sealed record AppEnvironmentVM(string WorkspaceLabel, string SecurityLabel);

public sealed record AppPageContextVM(
    string ModuleCode,
    string ModuleLabel,
    string Title,
    string? Subtitle,
    string Icon,
    IReadOnlyList<AppBreadcrumbItemVM> Breadcrumb,
    IReadOnlyList<AppContextStatusVM> Statuses);

public sealed record AppBreadcrumbItemVM(string Label, string? Controller = null, string? Action = null);
public sealed record AppContextStatusVM(string Label, string Tone = "neutral");

public sealed record AppUserShellVM(string DisplayName, string Initials, string RoleCode, string RoleLabel, string? Sector, bool ShowSectorWarning);
public sealed record AppMenuSectionVM(string Id, string Label, IReadOnlyList<AppMenuItemVM> Items);

/// <summary>Declarative, testable route identity used by both desktop and mobile navigation.</summary>
public sealed record AppMenuRouteRuleVM(
    string Controller,
    string Action,
    IDictionary<string, string>? RouteValues = null)
{
    public bool Matches(string? controller, string? action, IReadOnlyDictionary<string, string?> routeValues)
    {
        if (!string.Equals(Controller, controller, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Action, action, StringComparison.OrdinalIgnoreCase)) return false;

        return RouteValues is null || RouteValues.All(expected =>
            routeValues.TryGetValue(expected.Key, out var actual) &&
            string.Equals(expected.Value, actual, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record AppMenuItemVM(
    string Id,
    string Section,
    string Label,
    string Description,
    string Icon,
    string Controller,
    string Action,
    IDictionary<string, string> RouteValues,
    string? Permission,
    string? FeatureFlag,
    int Order,
    bool MobileVisible,
    AppMenuRouteRuleVM ActiveRouteRule,
    IReadOnlyList<string> Keywords);
public sealed record AppQuickActionVM(string Code, string Label, string Description, string Controller, string Action, string Icon, bool IsPrimary);
public sealed record AppUtilityActionVM(string Code, string Label, string Controller, string Action, string Icon);
public sealed record AppPrimaryActionVM(string Label, string Controller, string Action, string Icon);
