namespace InovaGed.Application.PhysicalArchive2;

public sealed record TransitSectorOption(Guid Id, string Name, string? Sigla);
public sealed record TransitCatalogItem(Guid Id, string Code, string? Title, bool IsOut);
public sealed record TransitRejectedItem(string DocCode, string Reason);
public sealed record TransitCheckoutResult(Guid BatchId, string BatchNumber, int AcceptedCount, IReadOnlyList<TransitRejectedItem> Rejected);
public sealed record TransitCounts(long OpenBatches, long OpenItems, long OverdueItems, long ReturnedToday);
public sealed record TransitBatchRow(Guid Id, string Number, string SectorName, string? CarrierName, string? CarrierId, int ItemsTotal, int ItemsReturned, int ItemsLost, string Status, DateTimeOffset CreatedAt, DateTimeOffset? DueAt, string? CreatorName, bool Overdue);
public sealed record TransitLoanRow(Guid Id, Guid? DocumentId, string DocCode, string? DocTitle, string Status, string? CarrierName, DateTimeOffset LoanedAt, DateTimeOffset? DueAt, DateTimeOffset? ReturnedAt, string? LostReason);
public sealed record TransitCustodyRow(DateTimeOffset OccurredAt, string EventType, string Title, string? Description, string? ActorName);
public sealed record TransitBatchQuery(TransitCounts Counts, IReadOnlyList<TransitBatchRow> Rows);
public sealed record TransitBatchDetails(TransitBatchRow Batch, IReadOnlyList<TransitLoanRow> Items);
public sealed record TransitLostResult(bool Ok, string? Error, Guid? BatchId);
public sealed record TransitDocumentHistory(TransitCatalogItem? Doc, IReadOnlyList<TransitCustodyRow> Events);

/// <summary>
/// Bloco B: trânsito físico de prontuários (DAME/arquivo central ↔ setores hospitalares).
/// Lotes de saída por documento com cautela, devolução (parcial/total), extravio e histórico cronológico.
/// </summary>
public interface IPhysicalTransitService
{
    Task<IReadOnlyList<TransitSectorOption>> SectorsAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<TransitCatalogItem>> CatalogAsync(Guid tenantId, string? q, CancellationToken ct);
    Task<TransitCheckoutResult> CheckoutAsync(Guid tenantId, Guid? sectorId, string? sectorName, string? carrierName, string? carrierId, string? reason, IReadOnlyList<Guid> documentIds, Guid? userId, string? userName, DateTimeOffset? dueAt, CancellationToken ct);
    Task<bool> CheckinAsync(Guid tenantId, Guid batchId, IReadOnlyList<Guid> loanIds, string? notes, Guid? userId, string? userName, CancellationToken ct);
    Task<TransitLostResult> MarkLostAsync(Guid tenantId, Guid loanId, string? justification, Guid? userId, string? userName, CancellationToken ct);
    Task<TransitBatchQuery> BatchesAsync(Guid tenantId, Guid? sectorId, string? statusFilter, string? q, CancellationToken ct);
    Task<TransitBatchDetails?> BatchDetailAsync(Guid tenantId, Guid batchId, CancellationToken ct);
    Task<TransitDocumentHistory> DocumentHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
