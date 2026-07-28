namespace InovaGed.Web.Models.Administration;

public sealed record AdministrationActionVM(
    string Title,
    string Description,
    string Icon,
    string Controller,
    string Action,
    string Policy,
    string Category,
    bool Available,
    string? UnavailableReason);

public sealed record AdministrationSectionVM(string Title, string Description, IReadOnlyList<AdministrationActionVM> Actions);

public sealed record AdministrationHealthVM(string Title, string? Value, string Status, string? Message, string Icon);

public sealed record AdministrationDashboardVM(
    IReadOnlyList<AdministrationHealthVM> Health,
    IReadOnlyList<AdministrationSectionVM> Sections,
    IReadOnlyList<AdministrationRecommendationVM> Recommendations);

public sealed record AdministrationRecommendationVM(string Title, string Reason, string Guidance, string Severity);
