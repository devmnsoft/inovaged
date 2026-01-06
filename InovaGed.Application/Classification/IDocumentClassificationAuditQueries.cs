namespace InovaGed.Application.Classification
{
    public interface IDocumentClassificationAuditQueries
    {
        Task<IReadOnlyList<ClassificationAuditRowDto>> ListByDocumentAsync(
            Guid tenantId,
            Guid documentId,
            int take,
            CancellationToken ct);
    }
}
