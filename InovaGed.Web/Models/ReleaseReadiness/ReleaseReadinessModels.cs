namespace InovaGed.Web.Models.ReleaseReadiness;

public static class ModuleReadinessStatus
{
    public const string Ready = "READY";
    public const string NeedsMigration = "NEEDS_MIGRATION";
    public const string NeedsConfiguration = "NEEDS_CONFIGURATION";
    public const string InImplementation = "IN_IMPLEMENTATION";
    public const string Blocked = "BLOCKED";
    public const string Disabled = "DISABLED";
}

public sealed record ModuleAvailability(bool Enabled, string? Reason = null, string? TechnicalForecast = null);

public sealed record ModuleReadinessItem(
    string Code, string Name, string Description, string Status, string MainRoute,
    string RequiredPermission, string DatabaseDependencies, string DiDependencies,
    string? LastIncident, string RecommendedAction);

public sealed record ReleaseReadinessViewModel(
    IReadOnlyList<ModuleReadinessItem> Modules,
    int PendingMigrations,
    DateTimeOffset CheckedAtUtc)
{
    public int Ready => Modules.Count(x => x.Status == ModuleReadinessStatus.Ready);
    public int Blocked => Modules.Count(x => x.Status == ModuleReadinessStatus.Blocked);
    public int InImplementation => Modules.Count(x => x.Status is ModuleReadinessStatus.InImplementation or ModuleReadinessStatus.Disabled);
    public bool IsReady => PendingMigrations == 0 && Blocked == 0;
}

public sealed record ModuleUnderConstructionViewModel(
    string Status, string Reason, string? TechnicalForecast, string RecommendedAction);
