namespace InovaGed.Application.Ged.Classification;

public interface IGedClassificationSuggestionService
{
    Task<ClassificationSuggestionDto?> SuggestForDocumentAsync(Guid tenantId, Guid documentId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<ClassificationSuggestionDto>> SuggestBatchAsync(Guid tenantId, IReadOnlyList<Guid> documentIds, Guid userId, CancellationToken ct);
}
