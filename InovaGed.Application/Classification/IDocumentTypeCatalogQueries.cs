namespace InovaGed.Application.Classification;

public interface IDocumentTypeCatalogQueries
{
    Task<IReadOnlyList<DocumentTypeItemDto>> ListAsync(Guid tenantId, CancellationToken ct);
}

public sealed class DocumentTypeItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}
