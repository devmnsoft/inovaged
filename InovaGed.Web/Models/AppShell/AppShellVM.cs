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
public sealed record AppMenuSectionVM(string Label, IReadOnlyList<AppMenuItemVM> Items);
public sealed record AppMenuItemVM(string Code, string Label, string Description, string Controller, string Action, string Icon, IReadOnlyList<string> Keywords, IReadOnlyList<string>? ActiveControllers = null);
public sealed record AppQuickActionVM(string Code, string Label, string Description, string Controller, string Action, string Icon, bool IsPrimary);
public sealed record AppUtilityActionVM(string Code, string Label, string Controller, string Action, string Icon);
public sealed record AppPrimaryActionVM(string Label, string Controller, string Action, string Icon);
