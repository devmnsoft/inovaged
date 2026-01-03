using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;

namespace InovaGed.Infrastructure.Documents;

public sealed class DocumentSearchTextQueries : IDocumentSearchTextQueries
{
    private readonly IDbConnectionFactory _db;
    public DocumentSearchTextQueries(IDbConnectionFactory db) => _db = db;

    public async Task<string?> GetOcrTextAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var con = await _db.OpenAsync(ct);

        const string sql = @"
SELECT ocr_text
FROM ged.document_search
WHERE tenant_id = @TenantId
  AND document_id = @DocumentId
LIMIT 1;
";
        return await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { TenantId = tenantId, DocumentId = documentId }, cancellationToken: ct));
    }
}
