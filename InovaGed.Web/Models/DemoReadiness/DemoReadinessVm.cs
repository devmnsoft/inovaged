namespace InovaGed.Web.Models.DemoReadiness;

public sealed class DemoReadinessVm
{
    public bool PresentationMode { get; set; }
    public int ReadinessScore { get; set; }
    public string OverallStatus { get; set; } = string.Empty;
    public string ExecutiveMessage { get; set; } = string.Empty;
    public List<ExecutiveValueIndicatorDto> ExecutiveValueIndicators { get; set; } = [];
    public List<DigitalMaturityItemDto> DigitalMaturity { get; set; } = [];
    public List<OcrSignalSummaryDto> OcrSignals { get; set; } = [];
    public List<FinancialImpactEstimateDto> FinancialImpacts { get; set; } = [];
    public List<ExecutiveAlertDto> Alerts { get; set; } = [];
    public List<DemoModuleStatusDto> ModuleMap { get; set; } = [];
    public List<DemoScriptStepDto> Script { get; set; } = [];
    public List<ExecutiveRecommendationDto> Recommendations { get; set; } = [];
}
public sealed class ExecutiveValueIndicatorDto { public string Title { get; set; }=string.Empty; public string Value { get; set; }=string.Empty; public string Subtitle{get;set;}=string.Empty; public string Icon{get;set;}="bi-star"; public string Color{get;set;}="primary"; public string Explanation{get;set;}=string.Empty; }
public sealed class DigitalMaturityItemDto { public string Label{get;set;}=string.Empty; public decimal Percentage{get;set;} public string Status{get;set;}=string.Empty; public string Recommendation{get;set;}=string.Empty; }
public sealed class OcrSignalSummaryDto { public string Term{get;set;}=string.Empty; public string Category{get;set;}=string.Empty; public int Count{get;set;} public string RiskLevel{get;set;}=string.Empty; }
public sealed class DemoModuleStatusDto { public string ModuleName{get;set;}=string.Empty; public string Status{get;set;}=string.Empty; public string Benefit{get;set;}=string.Empty; public string SuggestedDemoTime{get;set;}=string.Empty; public string ActionUrl{get;set;}=string.Empty; public string Recommendation{get;set;}=string.Empty; }
public sealed class DemoScriptStepDto { public int Order{get;set;} public string Title{get;set;}=string.Empty; public string SuggestedSpeech{get;set;}=string.Empty; public string ModuleUrl{get;set;}=string.Empty; public string Status{get;set;}=string.Empty; public int EstimatedMinutes{get;set;} }
public sealed class FinancialImpactEstimateDto { public string Title{get;set;}=string.Empty; public string Value{get;set;}=string.Empty; public string Explanation{get;set;}=string.Empty; public string ConfidenceLevel{get;set;}=string.Empty; }
public sealed class ExecutiveAlertDto { public string Severity{get;set;}=string.Empty; public string Impact{get;set;}=string.Empty; public string Title{get;set;}=string.Empty; public string Recommendation{get;set;}=string.Empty; public string ModuleUrl{get;set;}=string.Empty; }
public sealed class ExecutiveRecommendationDto { public string Priority{get;set;}=string.Empty; public string Reason{get;set;}=string.Empty; public string SuggestedAction{get;set;}=string.Empty; public string ModuleUrl{get;set;}=string.Empty; }
