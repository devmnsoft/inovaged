namespace InovaGed.Application.Retention;

public sealed class DestinationCreateRequest
{
    public string Destination { get; set; } = "ELIMINAR"; // ELIMINAR | TRANSFERIR | RECOLHER
    public string? Notes { get; set; }
    public Guid[] DocumentIds { get; set; } = Array.Empty<Guid>();
}

public sealed record DestinationBatchRow(
    Guid Id,
    string Destination,
    string Status,
    Guid? PcdVersionId,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? ExecutedAt,
    Guid? ExecutedBy
);

public sealed record DestinationItemRow(
    Guid BatchId,
    Guid DocumentId,
    string? DocCode,
    string? DocTitle,
    string? ClassificationCode,
    string? ClassificationName,
    DateTimeOffset? BasisAt,
    DateTimeOffset? DueAt,
    string? RetentionStatus,
    bool HoldActive,
    string? HoldReason
);

public interface IRetentionDestinationRepository
{
    Task<Guid> CreateBatchAsync(Guid tenantId, Guid userId, DestinationCreateRequest req, CancellationToken ct);
    Task<IReadOnlyList<DestinationBatchRow>> ListBatchesAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<DestinationItemRow>> GetBatchItemsAsync(Guid tenantId, Guid batchId, CancellationToken ct);

    Task<string> ExportBatchCsvAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);

    Task ExecuteBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
}

public interface IPcdVersionResolver
{
    Task<Guid?> GetLatestPublishedVersionIdAsync(Guid tenantId, CancellationToken ct);
}