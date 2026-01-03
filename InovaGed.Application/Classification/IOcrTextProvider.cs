namespace InovaGed.Application.Classification;

public interface IOcrTextProvider
{
    Task<string?> GetOcrTextAsync(Guid tenantId, Guid documentId, Guid ocrVersionId, CancellationToken ct);
}
