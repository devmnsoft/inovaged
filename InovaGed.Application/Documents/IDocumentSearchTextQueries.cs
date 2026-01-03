namespace InovaGed.Application.Documents;

public interface IDocumentSearchTextQueries
{
    Task<string?> GetOcrTextAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
