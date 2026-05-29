namespace InovaGed.Application.HospitalTrends;

public sealed class HospitalTrendsFilter
{
    public Guid TenantId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public DateTime? CompareFrom { get; set; }
    public DateTime? CompareTo { get; set; }
    public Guid? FolderId { get; set; }
    public string? Sector { get; set; }
    public string? DocumentType { get; set; }
    public string? Category { get; set; }
    public int Top { get; set; }
    public bool RefreshCache { get; set; }
}

public sealed class HospitalTrendsDashboardDto
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string PeriodLabel { get; set; } = string.Empty;
    public string ComparePeriodLabel { get; set; } = string.Empty;
    public int TotalDocumentsCurrent { get; set; }
    public int TotalDocumentsPrevious { get; set; }
    public decimal VariationPercent { get; set; }
    public int TotalAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public int WarningAlerts { get; set; }
    public List<TrendKpiDto> TermTrends { get; set; } = [];
    public List<HospitalAlertDto> Alerts { get; set; } = [];
    public List<SectorTrendDto> SectorTrends { get; set; } = [];
    public List<OperationalTrendDto> OperationalTrends { get; set; } = [];
    public List<RankingKpiDto> TopFolders { get; set; } = [];
    public List<RankingKpiDto> TopDocumentTypes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class TrendKpiDto
{
    public string Term { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int CurrentCount { get; set; }
    public int PreviousCount { get; set; }
    public decimal VariationPercent { get; set; }
    public string TrendDirection { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string Interpretation { get; set; } = string.Empty;
    public List<DocumentSnippetDto> Examples { get; set; } = [];
}

public sealed class HospitalAlertDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public int RelatedDocumentCount { get; set; }
    public string? ActionUrl { get; set; }
    public List<DocumentSnippetDto> Examples { get; set; } = [];
}

public sealed class SectorTrendDto
{
    public string Sector { get; set; } = string.Empty;
    public int CurrentDocuments { get; set; }
    public int PreviousDocuments { get; set; }
    public decimal VariationPercent { get; set; }
    public int PendingOcr { get; set; }
    public int Unclassified { get; set; }
    public string Interpretation { get; set; } = string.Empty;
}

public sealed class OperationalTrendDto
{
    public string Indicator { get; set; } = string.Empty;
    public int CurrentValue { get; set; }
    public int PreviousValue { get; set; }
    public decimal VariationPercent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
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

public sealed class RankingKpiDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
    public decimal Percentage { get; set; }
}
