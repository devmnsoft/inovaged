namespace InovaGed.Application.Ged.Documents;

public sealed record DocumentBulkClassificationItem(Guid DocumentId, bool Success, string Message);
public sealed record DocumentBulkClassificationResult(int Requested, int Succeeded, int Failed,
    IReadOnlyList<DocumentBulkClassificationItem> Items);

public interface IDocumentBulkClassificationService
{
    Task<DocumentBulkClassificationResult> ApplyAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> documentIds, Guid classificationId, CancellationToken ct);
}
