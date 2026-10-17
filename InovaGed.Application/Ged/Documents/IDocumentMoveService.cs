using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Documents;

public interface IDocumentMoveService
{
    Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, bool isAdmin, CancellationToken ct);
    Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, bool isAdmin, string? correlationId, string? idempotencyKey, CancellationToken ct);
    Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, CancellationToken ct);
    Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, bool allOrNothing, string? idempotencyKey, string? correlationId, CancellationToken ct);
    Task<IReadOnlyList<FolderOptionDto>> SearchFoldersAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct);
    Task<IReadOnlyList<DocumentMoveHistoryDto>> GetMoveHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
