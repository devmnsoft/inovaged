namespace InovaGed.Application.SystemHealth;

public interface IHomologationHealthService
{
    Task<HomologationReportDto> GenerateAsync(string? generatedBy, CancellationToken ct);
}

public sealed class HomologationReportDto
{
    public bool IsReadyForPresentation { get; set; }
    public int CriticalIssues { get; set; }
    public int WarningIssues { get; set; }
    public int PassedChecks { get; set; }
    public List<HomologationCheckDto> Checks { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Environment { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string GeneratedBy { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
}

public sealed class HomologationCheckDto
{
    public string Area { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public bool IsAutomatic { get; set; }
}
