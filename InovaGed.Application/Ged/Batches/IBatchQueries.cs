using InovaGed.Application.Ged.Loans;

namespace InovaGed.Application.Ged.Batches;

public interface IBatchQueries
{
    Task<IReadOnlyList<BatchRowDto>> ListAsync(Guid tenantId, string? q, string? status, CancellationToken ct);
    Task<(BatchRowDto Header, List<BatchItemDto> Items, List<BatchHistoryDto> History)?> GetAsync(Guid tenantId, Guid batchId, CancellationToken ct);

    Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string? q, int take, string? status, CancellationToken ct);

    Task<IReadOnlyList<DocumentSearchDto>> SearchDocumentsAsync(Guid tenantId, string q, int take, CancellationToken ct);
}