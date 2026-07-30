namespace InovaGed.Web.Models.AppShell;

public sealed record AppShellVM(
    string PageTitle,
    string? PageSubtitle,
    AppUserShellVM User,
    IReadOnlyList<AppMenuSectionVM> Menu,
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
    string Label,
    string Controller,
    string Action,
    string Icon,
    IReadOnlyList<string>? ActiveControllers = null);

public sealed record AppPrimaryActionVM(string Label, string Controller, string Action, string Icon);
