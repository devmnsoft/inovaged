using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class DocumentTypeQueries : IDocumentTypeQueries
{
    private readonly IDbConnectionFactory _db;

    public DocumentTypeQueries(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<Guid?> GetIdByCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken ct)
    {
        const string sql = @"
SELECT id
FROM ged.document_type
WHERE tenant_id = @tenantId
  AND code = @code
  AND reg_status = 'A'
LIMIT 1;
";

        var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, new { tenantId, code }, cancellationToken: ct));
    }
}
