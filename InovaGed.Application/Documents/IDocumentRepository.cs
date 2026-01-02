using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;

namespace InovaGed.Application.Documents;

public interface IDocumentRepository
{
    Task<Guid> CreateDocumentAsync(Document doc, CancellationToken ct);
    Task<Guid> CreateVersionAsync(DocumentVersion version, CancellationToken ct);

    Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    Task SetCurrentVersionAsync(Guid tenantId, Guid documentId, Guid versionId, Guid userId, CancellationToken ct);
    Task UpdateInfoAsync(Guid tenantId, Guid documentId, string title, string? description, bool confidential, Guid userId, CancellationToken ct);
}
