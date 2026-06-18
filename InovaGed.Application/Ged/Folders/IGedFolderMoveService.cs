using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Folders;

public interface IGedFolderMoveService
{
    Task<Result<MoveFolderResult>> MoveAsync(
        Guid tenantId,
        Guid userId,
        MoveFolderRequest request,
        CancellationToken ct);

    Task<IReadOnlyList<MoveFolderTarget>> GetMoveTargetsAsync(
        Guid tenantId,
        Guid userId,
        Guid folderId,
        bool includeRoot,
        CancellationToken ct);
}
