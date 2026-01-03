public sealed record UnclassifiedRowDto(
    Guid Id,
    string Title,
    string FileName,
    Guid? FolderId,
    string? FolderName,
    DateTime CreatedAt);

public interface IClassificationDashboardQueries
{
    Task<int> CountAsync(Guid tenantId, Guid? folderId, CancellationToken ct);
    Task<IReadOnlyList<(Guid? FolderId, string FolderName, int Count)>> ByFolderAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<UnclassifiedRowDto>> ListAsync(Guid tenantId, Guid? folderId, int page, int pageSize, CancellationToken ct);
}
