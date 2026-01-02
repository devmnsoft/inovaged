namespace InovaGed.Application.Classification;

public sealed class DocumentClassificationViewDto
{
    public Guid DocumentId { get; init; }
    public Guid TenantId { get; init; }

    public Guid? DocumentTypeId { get; init; }
    public string? DocumentTypeName { get; init; }

    public decimal? Confidence { get; init; }
    public string Method { get; init; } = "RULES";
    public string? Summary { get; init; }

    public DateTimeOffset ClassifiedAt { get; init; }

    public List<string> Tags { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new(); 

    public Guid? SuggestedTypeId { get; set; }
    public string? SuggestedTypeName { get; set; }
    public decimal? SuggestedConfidence { get; set; }
    public string? SuggestedSummary { get; set; }
    public DateTimeOffset? SuggestedAt { get; set; }

}
