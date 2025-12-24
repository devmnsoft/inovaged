
using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;

namespace InovaGed.Application.Documents;
public interface IDocumentQueries
{
    Task<IReadOnlyList<DocumentRowDto>> ListAsync(
        Guid tenantId, Guid? folderId, string? q, CancellationToken ct);

    Task<DocumentDetailsDto?> GetAsync(
        Guid tenantId, Guid documentId, CancellationToken ct);

    Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(
        Guid tenantId, Guid documentId, CancellationToken ct);

    Task<DocumentVersionDownloadDto?> GetVersionForDownloadAsync(
        Guid tenantId, Guid versionId, CancellationToken ct);
}

