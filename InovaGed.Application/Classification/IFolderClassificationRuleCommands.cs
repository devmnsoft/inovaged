
namespace InovaGed.Application.Classification;

public interface IFolderClassificationRuleCommands
{
    Task SetDefaultTypeAsync(Guid tenantId, Guid folderId, Guid documentTypeId, Guid? userId, CancellationToken ct);
    Task ClearDefaultTypeAsync(Guid tenantId, Guid folderId, CancellationToken ct);
}
