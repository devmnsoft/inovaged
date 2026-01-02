namespace InovaGed.Application.Classification;

public interface IDocumentClassificationQueries
{
    Task<DocumentClassificationViewDto?> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct);
     
    // ✅ Novo: usado para travar workflow e KPI
    Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    // ✅ Novo: KPI (opcional filtrar por pasta)
    Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct);

}
