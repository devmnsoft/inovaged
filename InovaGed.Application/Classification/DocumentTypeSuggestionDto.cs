namespace InovaGed.Application.Classification;

public sealed record DocumentTypeSuggestionDto(
    Guid? TypeId,
    string? TypeName,
    decimal? Confidence,
    string? Summary
);
