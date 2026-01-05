using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;

namespace InovaGed.Infrastructure.Documents;

public sealed class DocumentSearchIndex : IDocumentSearchIndex
{
    private readonly IDbConnectionFactory _db;
    public DocumentSearchIndex(IDbConnectionFactory db) => _db = db;

    public async Task UpsertOcrTextAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string fileName,
        string? ocrText,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.document_search
(tenant_id, document_id, version_id, file_name, ocr_text, search_vector, created_at, updated_at)
VALUES
(@TenantId, @DocumentId, @VersionId, @FileName, @OcrText,
 to_tsvector('simple', coalesce(@OcrText,'')),
 now(), now())
ON CONFLICT (tenant_id, document_id)
DO UPDATE SET
  version_id = EXCLUDED.version_id,
  file_name = EXCLUDED.file_name,
  ocr_text = EXCLUDED.ocr_text,
  search_vector = to_tsvector('simple', coalesce(EXCLUDED.ocr_text,'')),
  updated_at = now();
";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            TenantId = tenantId,
            DocumentId = documentId,
            VersionId = versionId,
            FileName = fileName,
            OcrText = ocrText
        }, cancellationToken: ct));
    }
}
