using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged;

public interface IFolderCommands
{
    Task<Result<Guid>> CreateAsync(
        Guid tenantId,
        string name,
        Guid? parentId,
        Guid? departmentId,
        Guid? createdBy,
        CancellationToken ct);

    Task<Result> RenameAsync(
        Guid tenantId,
        Guid folderId,
        string newName,
        CancellationToken ct);


    Task DeactivateAsync(Guid tenantId, Guid folderId, Guid? userId, CancellationToken ct);
    Task<Result> DeleteRecursiveAsync(Guid tenantId, Guid folderId, Guid? userId, CancellationToken ct);


}
