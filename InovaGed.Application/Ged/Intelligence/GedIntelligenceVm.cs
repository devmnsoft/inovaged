namespace InovaGed.Application.Ged.Intelligence;

public sealed class GedIntelligenceVm
{
    public IReadOnlyList<GedKpiVm> Kpis { get; set; } = [];
    public IReadOnlyList<GedHealthIndexVm> HealthIndexes { get; set; } = [];
    public IReadOnlyList<string> Insights { get; set; } = [];
    public IReadOnlyList<GedRankVm> TopFolders { get; set; } = [];
    public IReadOnlyList<GedRankVm> TopOcrErrorFolders { get; set; } = [];
    public IReadOnlyList<GedRankVm> TopUsers { get; set; } = [];
    public IReadOnlyList<GedRankVm> LastFailedUploads { get; set; } = [];
    public IReadOnlyList<GedRankVm> OldIncompleteDocuments { get; set; } = [];
    public IReadOnlyList<GedRankVm> DocumentsWithoutOcr { get; set; } = [];
    public IReadOnlyList<GedRankVm> DocumentsWithoutClassification { get; set; } = [];
    public IReadOnlyList<GedRankVm> SmartSearchWithoutResult { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public sealed class GedKpiVm
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = "0";
    public string Hint { get; set; } = string.Empty;
    public string Css { get; set; } = "primary";
}

public sealed class GedHealthIndexVm
{
    public string Name { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public string Status { get; set; } = "Atenção";
    public string Suggestion { get; set; } = string.Empty;
}

public sealed class GedRankVm
{
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int Count { get; set; }
}

public interface IGedAdministrativeIntelligenceService
{
    Task<GedIntelligenceVm> GetAsync(Guid tenantId, CancellationToken ct);
}
