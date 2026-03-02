namespace InovaGed.Application.Ged.Reports;

public sealed class ReportRunCreateVM
{
    public string ReportType { get; set; } = "GENERIC";
    public List<Guid> DocumentIds { get; set; } = new();
    public Dictionary<string, object>? Parameters { get; set; }
    public string? Notes { get; set; }
}

public sealed record ReportRunRowDto(
    Guid Id,
    string ReportType,
    DateTimeOffset GeneratedAt,
    string? Notes,
    int ItemsCount);

public interface IReportService
{
    // Cria report_run + report_run_signature baseado no "snapshot" de assinatura
    Task<InovaGed.Domain.Primitives.Result<Guid>> CreateReportRunWithSignatureSnapshotAsync(
        Guid tenantId,
        Guid? userId,
        ReportRunCreateVM vm,
        CancellationToken ct);
}