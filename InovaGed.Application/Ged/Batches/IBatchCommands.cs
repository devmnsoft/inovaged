using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Batches;

public interface IBatchCommands
{
    Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, BatchCreateVM vm, CancellationToken ct);
    Task<Result> AddItemAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? boxId, Guid? userId, CancellationToken ct);
    Task<Result> MoveItemBoxAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? newBoxId, Guid? userId, CancellationToken ct);
    Task<Result> RemoveItemAsync(Guid tenantId, Guid batchId, Guid documentId, Guid? userId, CancellationToken ct);
    Task<Result> ChangeStatusAsync(Guid tenantId, Guid batchId, string newStatus, Guid? userId, string? notes, CancellationToken ct);
}