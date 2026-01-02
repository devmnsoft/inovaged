namespace InovaGed.Application.Classification;

public interface IDocumentClassificationRepository
{
    Task UpsertClassificationAsync(
        Guid tenantId,
        Guid documentId,
        Guid documentVersionId,
        Guid? documentTypeId,
        decimal? confidence,
        string method,
        string? summary,
        Guid? classifiedBy,
        CancellationToken ct);

    Task UpsertTagsAsync(
        Guid tenantId,
        Guid documentId,
        IReadOnlyList<string> tags,
        string method,
        Guid? assignedBy,
        CancellationToken ct);

    Task UpsertMetadataAsync(
        Guid tenantId,
        Guid documentId,
        IReadOnlyDictionary<string, (string Value, decimal? Confidence)> metadata,
        string method,
        CancellationToken ct);

    // PASSO 12 (sync manual)
    Task ReplaceTagsAsync(Guid tenantId, Guid documentId, IReadOnlyList<string> tags, string method, Guid? assignedBy, CancellationToken ct);
    Task ReplaceMetadataAsync(Guid tenantId, Guid documentId, IReadOnlyDictionary<string, (string Value, decimal? Confidence)> metadata, string method, CancellationToken ct);

    Task SetSuggestionAsync(Guid tenantId, Guid documentId, Guid? suggestedTypeId, decimal? suggestedConfidence, string? suggestedSummary, DateTimeOffset? suggestedAt, CancellationToken ct);
    Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct);

}
