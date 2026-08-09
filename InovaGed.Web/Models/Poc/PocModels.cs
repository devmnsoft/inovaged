using InovaGed.Web.Models.Atlas;

namespace InovaGed.Web.Models.Poc;

public enum PocReadinessStatus { Ready, Partial, Pending, Error }

public sealed record PocModuleVm(
    string Key, string Name, string Description, string Icon, PocReadinessStatus Status,
    int Coverage, string Url, string QuickActionLabel, string QuickActionUrl,
    string Evidence, DateTimeOffset LastValidatedAt);

public sealed record PocChecklistItemVm(
    int Number, string Requirement, string ProofScreen, string TechnicalReference,
    PocReadinessStatus Status, string Evidence, string DemoStep);

public sealed record PocDemoStepVm(
    int Order, string Title, string Description, string Url, string ActionLabel,
    int Minutes, string ExpectedEvidence, string Icon);

public sealed record PocEvidenceVm(
    string Module, string Evidence, string Route, string TechnicalReference,
    PocReadinessStatus Status, DateTimeOffset ValidatedAt);

public sealed class PocDashboardVm
{
    public required IReadOnlyList<PocModuleVm> Modules { get; init; }
    public required IReadOnlyList<AtlasMetricVm> Metrics { get; init; }
    public int OverallCoverage => Modules.Count == 0 ? 0 : (int)Math.Round(Modules.Average(x => x.Coverage));
}

public sealed record PocChecklistVm(IReadOnlyList<PocChecklistItemVm> Items, IReadOnlyList<AtlasMetricVm> Metrics);
public sealed record PocDemoVm(IReadOnlyList<PocDemoStepVm> Steps, int TotalMinutes);
public sealed record PocEvidencesVm(IReadOnlyList<PocEvidenceVm> Items);
