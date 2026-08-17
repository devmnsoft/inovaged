using InovaGed.Web.Models.Atlas;

namespace InovaGed.Web.Models.Reports;

public sealed record ReportCatalogItem(
    string Title,
    string Description,
    string Icon,
    string Url,
    string Category,
    bool ExportAvailable = false);

public sealed class ReportsHubVm
{
    public IReadOnlyList<ReportCatalogItem> Items { get; init; } = [];
    public IReadOnlyList<AtlasMetricVm> Metrics { get; init; } = [];
    public IReadOnlyList<ReportBreakdownRow> DocumentsByArea { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ReportBreakdownRow(string Label, long Total, decimal Percentage);
