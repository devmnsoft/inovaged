using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InovaGed.Infrastructure;

public interface IModuleCatalog
{
    IReadOnlyCollection<ModuleCatalogEntry> GetModules();
}

public sealed record ModuleCatalogEntry(
    string Name,
    bool Enabled,
    IReadOnlyCollection<string> Dependencies,
    bool ConfigurationValid,
    HealthStatus Health,
    string Version,
    string? LastFailure);

public sealed class ModuleCatalog : IModuleCatalog
{
    private static readonly ModuleCatalogEntry[] Modules =
    [
        new("Database", true, Array.Empty<string>(), true, HealthStatus.Healthy, "1.0.0", null),
        new("Ged", true, ["Database", "Storage"], true, HealthStatus.Healthy, "1.0.0", null),
        new("OCR", true, ["Database", "Storage"], true, HealthStatus.Degraded, "1.0.0", null),
        new("Preview", true, ["Storage", "LibreOffice"], true, HealthStatus.Degraded, "1.0.0", null),
        new("Classification", true, ["Ged", "OCR"], true, HealthStatus.Healthy, "1.0.0", null),
        new("Retention", true, ["Ged", "Database"], true, HealthStatus.Healthy, "1.0.0", null),
        new("Loans", true, ["Ged", "Users"], true, HealthStatus.Healthy, "1.0.0", null),
        new("Guardian", true, ["Ged", "OCR", "Audit"], true, HealthStatus.Healthy, "1.0.0", null),
        new("Workflow", true, ["Ged", "Users"], true, HealthStatus.Healthy, "1.0.0", null),
        new("SecurityOperations", true, ["Audit", "Users"], true, HealthStatus.Healthy, "1.0.0", null)
    ];

    public IReadOnlyCollection<ModuleCatalogEntry> GetModules() => Modules;
}
