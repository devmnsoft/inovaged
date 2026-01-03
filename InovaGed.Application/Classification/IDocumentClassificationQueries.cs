namespace InovaGed.Application.Classification;

public interface IDocumentClassificationQueries
{
    Task<DocumentClassificationViewDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct);

    Task<IReadOnlyList<DocumentTypeRowDto>> ListTypesAsync(Guid tenantId, CancellationToken ct);
}
