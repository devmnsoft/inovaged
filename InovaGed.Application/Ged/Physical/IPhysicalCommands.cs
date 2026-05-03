using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Physical;

public interface IPhysicalCommands
{
    Task<Result<Guid>> UpsertLocationAsync(Guid tenantId, Guid? userId, PhysicalLocationFormVM vm, CancellationToken ct);
    Task<Result> DeleteLocationAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct);
    Task<Result<Guid>> UpsertBoxAsync(Guid tenantId, Guid? userId, BoxFormVM vm, CancellationToken ct);
    Task<Result> DeleteBoxAsync(Guid tenantId, Guid id, Guid? userId, CancellationToken ct);
    Task<Result> AddDocumentToBoxAsync(Guid tenantId, Guid? userId, BoxContentMaintenanceVM vm, CancellationToken ct);
    Task<Result> RemoveDocumentFromBoxAsync(Guid tenantId, Guid? userId, BoxContentMaintenanceVM vm, CancellationToken ct);
    Task<Result> MoveDocumentToBoxAsync(Guid tenantId, Guid? userId, BoxContentMaintenanceVM vm, CancellationToken ct);
}
