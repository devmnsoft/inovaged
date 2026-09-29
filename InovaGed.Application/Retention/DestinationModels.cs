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
    string? HoldReason,
    string? BlockReason = null
);

public interface IRetentionDestinationRepository
{
    Task<Guid> CreateBatchAsync(Guid tenantId, Guid userId, DestinationCreateRequest req, CancellationToken ct);
    Task<IReadOnlyList<DestinationBatchRow>> ListBatchesAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<DestinationItemRow>> GetBatchItemsAsync(Guid tenantId, Guid batchId, CancellationToken ct);

    Task<string> ExportBatchCsvAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);

    Task<ExecuteBatchResult> ExecuteBatchAsync(Guid tenantId, Guid userId, Guid batchId, CancellationToken ct);
}

/// <summary>
/// Resultado da execução de um lote de destinação (Bloco C/C2).
/// Blocked &gt; 0 indica itens que NÃO executaram por empréstimo/movimentação/protocolo/hold ativo.
/// </summary>
public sealed record ExecuteBatchResult(bool AlreadyExecuted, int Executed, int Blocked);

public interface IPcdVersionResolver
{
    Task<Guid?> GetLatestPublishedVersionIdAsync(Guid tenantId, CancellationToken ct);
}