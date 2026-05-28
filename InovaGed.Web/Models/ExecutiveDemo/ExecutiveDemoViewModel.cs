namespace InovaGed.Web.Models.ExecutiveDemo;

public sealed class ExecutiveDemoViewModel
{
    public ExecutiveOverviewMetrics Overview { get; set; } = new();
    public IReadOnlyList<ExecutiveValueCard> ValueCards { get; set; } = [];
    public IReadOnlyList<ExecutiveScriptStep> ScriptSteps { get; set; } = [];
    public IReadOnlyList<ExecutiveReadinessAlert> ReadinessAlerts { get; set; } = [];
    public IReadOnlyList<string> ExecutivePhrases { get; set; } = [];
}

public sealed class ExecutiveOverviewMetrics
{
    public int TotalDocuments { get; set; }
    public int DocumentsWithOcr { get; set; }
    public int DocumentsPendingOcr { get; set; }
    public int UnclassifiedDocuments { get; set; }
    public int MovedDocuments { get; set; }
    public int DocumentRequests { get; set; }
    public int ActiveUsers { get; set; }
}

public sealed class ExecutiveValueCard
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-star";
}

public sealed class ExecutiveScriptStep
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ExecutiveExplanation { get; set; } = string.Empty;
    public string PracticalBenefit { get; set; } = string.Empty;
    public string ModuleUrl { get; set; } = "/";
}

public sealed class ExecutiveReadinessAlert
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
}
