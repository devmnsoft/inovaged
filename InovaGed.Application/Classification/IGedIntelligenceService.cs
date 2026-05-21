namespace InovaGed.Application.Classification;

public interface IGedIntelligenceService
{
    Task<IReadOnlyList<Guid>> DetectDuplicatesAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<DocumentTypeSuggestionDto>> SuggestTagsAsync(Guid tenantId, string text, CancellationToken ct);
    Task<IReadOnlyList<DocumentTypeSuggestionDto>> SuggestTagsAsync(Guid tenantId, Guid documentId, Guid? userId, string text, string? fileName, string? folderName, string? title, CancellationToken ct);
}
