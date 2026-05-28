namespace InovaGed.Application.DemoReadiness;

public sealed class DemoReadinessReportDto
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string OverallStatus { get; init; } = "OK";
    public int TotalChecks { get; init; }
    public int OkCount { get; init; }
    public int WarningCount { get; init; }
    public int ErrorCount { get; init; }
    public IReadOnlyList<DemoReadinessCheckDto> Checks { get; init; } = [];
    public IReadOnlyList<DemoReadinessRecommendationDto> Recommendations { get; init; } = [];
}
public sealed class DemoReadinessCheckDto
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Status { get; init; } = "SKIPPED";
    public string Message { get; init; } = string.Empty;
    public string? TechnicalDetail { get; init; }
    public long ElapsedMs { get; init; }
    public string Icon { get; init; } = "bi-dash-circle";
    public string Color { get; init; } = "secondary";
    public string? ActionUrl { get; init; }
}
public sealed class DemoReadinessRecommendationDto
{
    public string Priority { get; init; } = "MEDIUM";
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SuggestedAction { get; init; } = string.Empty;
}
