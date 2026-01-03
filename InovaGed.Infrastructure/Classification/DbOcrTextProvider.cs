using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Classification;

namespace InovaGed.Infrastructure.Classification;

public sealed class DbOcrTextProvider : IOcrTextProvider
{
    private readonly IDbConnectionFactory _db;

    public DbOcrTextProvider(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<string?> GetOcrTextAsync(Guid tenantId, Guid documentId, Guid ocrVersionId, CancellationToken ct)
    {
        const string sql = @"
SELECT COALESCE(NULLIF(ocr_text,''), NULLIF(content_text,''), '')
FROM ged.document_version
WHERE tenant_id=@tenantId AND document_id=@documentId
ORDER BY created_at DESC
LIMIT 1;";

        using var conn = await _db.OpenAsync(ct);
        var text = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
