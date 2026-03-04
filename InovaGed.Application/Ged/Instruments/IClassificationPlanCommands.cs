using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Instruments
{
    public interface IClassificationPlanCommands
    {
        Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, ClassificationPlanCreateVM vm, CancellationToken ct);
        Task<Result> UpdateAsync(Guid tenantId, Guid id, Guid? userId, ClassificationPlanUpdateVM vm, CancellationToken ct);
        Task<Result> MoveAsync(Guid tenantId, Guid? userId, ClassificationPlanMoveVM vm, CancellationToken ct);

        Task<Result<Guid>> PublishVersionAsync(Guid tenantId, Guid? userId, PublishClassificationPlanVersionVM vm, CancellationToken ct);
    }
}
