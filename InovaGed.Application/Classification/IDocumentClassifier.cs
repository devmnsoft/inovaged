namespace InovaGed.Application.Classification;

public interface IDocumentClassifier
{
    Task<DocumentClassificationResult> ClassifyAsync(
        Guid tenantId,
        Guid documentId,
        Guid documentVersionId,
        string ocrText,
        CancellationToken ct);
}
