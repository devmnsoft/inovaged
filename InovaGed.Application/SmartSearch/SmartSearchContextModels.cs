namespace InovaGed.Application.SmartSearch;

public interface ISmartSearchContextParser
{
    Task<SmartSearchContextIntent> ParseAsync(Guid tenantId, string query, CancellationToken ct);
}

public sealed class SmartSearchContextIntent
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string NormalizedQuery { get; set; } = string.Empty;
    public List<string> RequiredTerms { get; set; } = new();
    public List<string> OptionalTerms { get; set; } = new();
    public List<string> ClinicalTerms { get; set; } = new();
    public List<string> Synonyms { get; set; } = new();
    public List<string> RelatedTerms { get; set; } = new();
    public List<string> DocumentTypes { get; set; } = new();
    public List<string> PatientHints { get; set; } = new();
    public List<string> NumericTokens { get; set; } = new();
    public int? Year { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool HasSensitiveTerms { get; set; }
}

public sealed class SmartSearchReason
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public decimal Weight { get; set; }
}
