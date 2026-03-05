using InovaGed.Application.Ged.Loans;

namespace InovaGed.Application.Ged.Batches;

public interface IBatchQueries
{
    Task<IReadOnlyList<BatchRowDto>> ListAsync(Guid tenantId, string? q, string? status, CancellationToken ct);
    Task<(BatchRowDto Header, List<BatchItemDto> Items, List<BatchHistoryDto> History)?> GetAsync(Guid tenantId, Guid batchId, CancellationToken ct);

    // Item 17: busca de documentos para adicionar ao lote (sem folderId)
    Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string q, int limit, CancellationToken ct);

    // Item 17: busca de documentos com filtro de pasta opcional
    Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string? q, int limit, string? folderId, CancellationToken ct);
}