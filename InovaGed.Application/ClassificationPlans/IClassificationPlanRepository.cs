using InovaGed.Domain.Primitives;

namespace InovaGed.Application.ClassificationPlans;

public interface IClassificationPlanRepository
{
    Task<IReadOnlyList<ClassificationNodeRow>> ListTreeAsync(Guid tenantId, CancellationToken ct);
    Task<ClassificationEditVM?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task<Guid> UpsertAsync(Guid tenantId, Guid userId, ClassificationEditVM vm, CancellationToken ct);
    Task<Result> MoveAsync(Guid tenantId, Guid userId, Guid id, Guid? newParentId, CancellationToken ct);

    Task<IReadOnlyList<ClassificationVersionRow>> ListVersionsAsync(Guid tenantId, CancellationToken ct);
    Task<Guid> PublishVersionAsync(Guid tenantId, Guid userId, string title, string? notes, CancellationToken ct);
    Task<ClassificationVersionDetailsVM?> GetVersionAsync(Guid tenantId, Guid versionId, CancellationToken ct);
    Task<IReadOnlyList<ClassificationVersionItemRow>> ListVersionItemsAsync(Guid tenantId, Guid versionId, CancellationToken ct);
}