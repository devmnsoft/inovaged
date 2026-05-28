namespace InovaGed.Application.DemoReadiness;

public sealed class DemoReadinessReportDto
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string OverallStatus { get; set; } = "OK";
    public int ReadinessScore { get; set; }
    public int TotalChecks { get; set; }
    public int OkCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }

    public List<DemoReadinessCheckDto> Checks { get; set; } = new();
    public List<DemoReadinessRecommendationDto> Recommendations { get; set; } = new();

    public List<ExecutiveValueIndicatorDto> ExecutiveIndicators { get; set; } = new();
    public List<DigitalMaturityItemDto> DigitalMaturity { get; set; } = new();
    public List<OcrSignalSummaryDto> OcrSignals { get; set; } = new();
    public List<DemoModuleStatusDto> ModuleMap { get; set; } = new();
    public List<DemoScriptStepDto> ScriptSteps { get; set; } = new();
    public List<FinancialImpactEstimateDto> FinancialEstimates { get; set; } = new();

    public static DemoReadinessReportDto Empty(string message)
    {
        return new DemoReadinessReportDto
        {
            OverallStatus = "ERROR",
            ReadinessScore = 0,
            Checks =
            {
                new DemoReadinessCheckDto
                {
                    Code = "EMPTY",
                    Title = "Prontidão indisponível",
                    Module = "Sistema",
                    Status = "ERROR",
                    Message = message,
                    Icon = "bi-exclamation-triangle",
                    Color = "danger"
                }
            }
        };
    }
}

public sealed class DemoReadinessCheckDto { public string Code { get; set; } = ""; public string Title { get; set; } = ""; public string Module { get; set; } = ""; public string Status { get; set; } = "OK"; public string Message { get; set; } = ""; public string? TechnicalDetail { get; set; } public long ElapsedMs { get; set; } public string Icon { get; set; } = "bi-check-circle"; public string Color { get; set; } = "success"; public string? ActionUrl { get; set; } }
public sealed class DemoReadinessRecommendationDto { public string Priority { get; set; } = "Baixa"; public string Title { get; set; } = ""; public string Description { get; set; } = ""; public string SuggestedAction { get; set; } = ""; }
public sealed class ExecutiveValueIndicatorDto { public string Title { get; set; } = ""; public string Value { get; set; } = ""; public string Subtitle { get; set; } = ""; public string Icon { get; set; } = "bi-graph-up"; public string Color { get; set; } = "primary"; public string Explanation { get; set; } = ""; }
public sealed class DigitalMaturityItemDto { public string Label { get; set; } = ""; public decimal Percentage { get; set; } public string Status { get; set; } = ""; public string Recommendation { get; set; } = ""; }
public sealed class OcrSignalSummaryDto { public string Term { get; set; } = ""; public string Category { get; set; } = ""; public int Count { get; set; } public string RiskLevel { get; set; } = "Baixo"; }
public sealed class DemoModuleStatusDto { public string ModuleName { get; set; } = ""; public string Status { get; set; } = "OK"; public string Benefit { get; set; } = ""; public string SuggestedDemoTime { get; set; } = ""; public string? ActionUrl { get; set; } public string Recommendation { get; set; } = ""; }
public sealed class DemoScriptStepDto { public int Order { get; set; } public string Title { get; set; } = ""; public string SuggestedSpeech { get; set; } = ""; public string? ModuleUrl { get; set; } public string Status { get; set; } = "OK"; public int EstimatedMinutes { get; set; } }
public sealed class FinancialImpactEstimateDto { public string Title { get; set; } = ""; public string Value { get; set; } = ""; public string Explanation { get; set; } = ""; public string ConfidenceLevel { get; set; } = "Estimado"; }
