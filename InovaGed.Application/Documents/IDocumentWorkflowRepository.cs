using InovaGed.Domain.Documents;

namespace InovaGed.Application.Documents.Workflow;

public interface IDocumentWorkflowRepository
{
    Task<(DocumentStatus Status, bool Exists)> GetStatusAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    Task UpdateStatusAsync(Guid tenantId, Guid documentId, DocumentStatus toStatus, Guid? userId, CancellationToken ct);

    Task InsertLogAsync(Guid tenantId, Guid documentId, DocumentStatus? fromStatus, DocumentStatus toStatus,
        string? reason, Guid? userId, string? ipAddress, string? userAgent, CancellationToken ct);
}
