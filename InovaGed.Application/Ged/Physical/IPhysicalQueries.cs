namespace InovaGed.Application.Ged.Physical;

public interface IPhysicalQueries
{
    Task<IReadOnlyList<PhysicalLocationRowDto>> ListLocationsAsync(Guid tenantId, string? q, CancellationToken ct);
    Task<PhysicalLocationFormVM?> GetLocationAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<IReadOnlyList<BoxRowDto>> ListBoxesAsync(Guid tenantId, string? q, CancellationToken ct);
    Task<BoxFormVM?> GetBoxAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>Documentos atualmente dentro de uma caixa (via batch_item ativo).</summary>
    Task<IReadOnlyList<BoxContentItemDto>> GetBoxContentsAsync(Guid tenantId, Guid boxId, CancellationToken ct);

    Task<IReadOnlyList<BoxHistoryRowDto>> GetBoxHistoryAsync(Guid tenantId, Guid boxId, CancellationToken ct);
}