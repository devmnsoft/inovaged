using InovaGed.Domain.Ged;

namespace InovaGed.Application.Ged;

public interface IFolderQueries
{
    Task<IReadOnlyList<FolderNodeDto>> TreeAsync(Guid tenantId, CancellationToken ct);
}

