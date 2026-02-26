namespace InovaGed.Application.Classification;

public interface IDocumentTypeQueries
{
    Task<Guid?> GetIdByCodeAsync(Guid tenantId, string code, CancellationToken ct);

}
