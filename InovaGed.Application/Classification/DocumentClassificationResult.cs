namespace InovaGed.Application.Classification;

public sealed class DocumentClassificationResult
{
    public Guid? DocumentTypeId { get; init; }
    public decimal? Confidence { get; init; }
    public string Method { get; init; } = "RULES";
    public string? Summary { get; init; }

    public List<string> Tags { get; init; } = new();
    public Dictionary<string, (string Value, decimal? Confidence)> Metadata { get; init; } = new();
}
