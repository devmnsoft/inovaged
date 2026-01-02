using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentTypeCatalogQueries : IDocumentTypeCatalogQueries
{
    private readonly IDbConnectionFactory _db;

    public DocumentTypeCatalogQueries(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DocumentTypeItemDto>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT id AS Id, name AS Name
FROM ged.document_type
WHERE tenant_id = @tenantId
ORDER BY name;
";

        var conn = await _db.OpenAsync(ct);
        var items = await conn.QueryAsync<DocumentTypeItemDto>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

        return items.ToList();
    }
}
