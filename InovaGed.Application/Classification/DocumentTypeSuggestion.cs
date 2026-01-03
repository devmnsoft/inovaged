namespace InovaGed.Application.Classification;

public sealed class DocumentTypeSuggestion
{
    public Guid? TypeId { get; init; }
    public string? TypeName { get; init; }
    public decimal Confidence { get; init; }
    public string? Summary { get; init; }
    public List<string> Signals { get; init; } = new();
}
