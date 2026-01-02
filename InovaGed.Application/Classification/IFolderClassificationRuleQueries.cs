
namespace InovaGed.Application.Classification;

public interface IFolderClassificationRuleQueries
{
    Task<Guid?> GetDefaultTypeForFolderAsync(Guid tenantId, Guid folderId, CancellationToken ct);
}
