using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Documents;

public interface IDocumentCommands
{
    public Task<Result> DeleteAsync(Guid tenantId, Guid documentId, Guid? userId, CancellationToken ct);
    Task ApplyClassificationAsync(Guid tenantId, Guid userId, Guid documentId, Guid classificationId, CancellationToken ct);


}
