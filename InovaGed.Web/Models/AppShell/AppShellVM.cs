namespace InovaGed.Web.Models.AppShell;

public sealed record AppShellVM(
    string ModuleLabel,
    string PageTitle,
    string? PageSubtitle,
    AppUserShellVM User,
    IReadOnlyList<AppMenuSectionVM> Menu,
    IReadOnlyList<AppQuickActionVM> QuickActions,
    AppPrimaryActionVM? PrimaryAction);

public sealed record AppUserShellVM(
    string DisplayName,
    string Initials,
    string RoleCode,
    string RoleLabel,
    string? Sector,
    bool ShowSectorWarning);

public sealed record AppMenuSectionVM(string Label, IReadOnlyList<AppMenuItemVM> Items);

public sealed record AppMenuItemVM(
    string Code,
    string Label,
    string Description,
    string Controller,
    string Action,
    string Icon,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string>? ActiveControllers = null);

public sealed record AppQuickActionVM(
    string Code,
    string Label,
    string Description,
    string Controller,
    string Action,
    string Icon,
    bool IsPrimary);

public sealed record AppPrimaryActionVM(string Label, string Controller, string Action, string Icon);
