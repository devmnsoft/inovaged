using InovaGed.Domain.Ged;

namespace InovaGed.Application.Ged;

public interface IFolderQueries
{
    Task<IReadOnlyList<FolderNodeDto>> TreeAsync(Guid tenantId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid tenantId, Guid folderId, CancellationToken ct);
}

