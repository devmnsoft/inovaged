namespace InovaGed.Application.Documents;

public interface IDocumentSearchIndex
{
    Task UpsertOcrTextAsync(Guid tenantId, Guid documentId, Guid versionId, string fileName, string? ocrText, CancellationToken ct);
}
