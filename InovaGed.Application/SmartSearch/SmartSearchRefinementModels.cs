namespace InovaGed.Application.SmartSearch;

public sealed class SmartSearchQueryState
{
    public string Terms { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? Unit { get; set; }
    public string? Classification { get; set; }
    public int? Year { get; set; }
    public string DateField { get; set; } = "created";
    public string Sort { get; set; } = "relevance";
}

public sealed class SmartSearchRefinementResult
{
    public bool Supported { get; set; }
    public bool Ambiguous { get; set; }
    public string Intent { get; set; } = "unsupported";
    public string Description { get; set; } = string.Empty;
    public SmartSearchQueryState State { get; set; } = new();
    public IReadOnlyList<SmartSearchRefinementOption> Options { get; set; } = [];
}

public sealed class SmartSearchRefinementOption
{
    public string Label { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}

public interface ISmartSearchRefinementService
{
    SmartSearchRefinementResult Apply(SmartSearchQueryState current, string command);
}
