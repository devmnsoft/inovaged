using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Documents;

public interface IDocumentMoveService
{
    Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, CancellationToken ct);
    Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, CancellationToken ct);
    Task<IReadOnlyList<FolderOptionDto>> SearchFoldersAsync(Guid tenantId, string? term, CancellationToken ct);
    Task<IReadOnlyList<DocumentMoveHistoryDto>> GetMoveHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
