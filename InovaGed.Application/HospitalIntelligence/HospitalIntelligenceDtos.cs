namespace InovaGed.Application.HospitalIntelligence;

public sealed class HospitalIntelligenceFilter
{
    public Guid TenantId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? FolderId { get; set; }
    public string? Sector { get; set; }
    public string? DocumentType { get; set; }
    public string? Search { get; set; }
    public int Top { get; set; }
    public bool RefreshCache { get; set; }
}

public sealed class HospitalIntelligenceDashboardDto
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public int TotalDocuments { get; set; }
    public int DocumentsWithOcr { get; set; }
    public int DocumentsWithoutOcr { get; set; }
    public int OcrPending { get; set; }
    public int OcrProcessing { get; set; }
    public int OcrCompleted { get; set; }
    public int OcrErrors { get; set; }
    public int OcrCancelled { get; set; }
    public int UnclassifiedDocuments { get; set; }
    public int ClassifiedDocuments { get; set; }
    public int DocumentsWithFinancialSignals { get; set; }
    public int DocumentsWithClinicalSignals { get; set; }
    public int CriticalAlerts { get; set; }
    public decimal OcrCoveragePercent { get; set; }
    public decimal ClassificationCoveragePercent { get; set; }
    public decimal DataQualityScore { get; set; }
    public List<OcrKpiDto> OcrKpis { get; set; } = [];
    public List<ClinicalTermKpiDto> ClinicalTerms { get; set; } = [];
    public List<FinancialDocumentKpiDto> FinancialKpis { get; set; } = [];
    public List<OperationalKpiDto> OperationalKpis { get; set; } = [];
    public List<RiskAlertKpiDto> Alerts { get; set; } = [];
    public List<TimeSeriesKpiDto> DocumentsByMonth { get; set; } = [];
    public List<RankingKpiDto> DocumentsByFolder { get; set; } = [];
    public List<RankingKpiDto> DocumentsBySector { get; set; } = [];
    public List<RankingKpiDto> DocumentsByType { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class OcrKpiDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Percentage { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class ClinicalTermKpiDto
{
    public string Term { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Occurrences { get; set; }
    public int DocumentCount { get; set; }
    public decimal Percentage { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public List<DocumentSnippetDto> Examples { get; set; } = [];
}

public sealed class FinancialDocumentKpiDto
{
    public string Indicator { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public decimal? EstimatedValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public List<DocumentSnippetDto> Examples { get; set; } = [];
}

public sealed class OperationalKpiDto
{
    public string Indicator { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class RiskAlertKpiDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
}

public sealed class DocumentSnippetDto
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public sealed class TimeSeriesKpiDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class RankingKpiDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public decimal Percentage { get; set; }
}
