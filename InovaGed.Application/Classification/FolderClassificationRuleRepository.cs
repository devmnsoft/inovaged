using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Classification;

public sealed class FolderClassificationRuleRepository : IFolderClassificationRuleRepository
{
    private readonly IDbConnectionFactory _db;

    public FolderClassificationRuleRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<Guid?> GetDefaultTypeAsync(Guid tenantId, Guid folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT default_document_type_id
FROM ged.folder
WHERE tenant_id = @tenantId
  AND id = @folderId
  AND reg_status = 'A'
LIMIT 1;
";
        var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
    }

    public async Task SetDefaultTypeAsync(Guid tenantId, Guid folderId, Guid? documentTypeId, Guid? userId, CancellationToken ct)
    {
        // Se sua tabela folder não tem default_document_type_id, você precisa criar coluna.
        // Eu já estou usando ela porque é a forma mais simples/performática.
        const string sql = @"
UPDATE ged.folder
SET default_document_type_id = @documentTypeId,
    updated_at = NOW(),
    updated_by = @userId
WHERE tenant_id = @tenantId
  AND id = @folderId
  AND reg_status = 'A';
";
        var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, folderId, documentTypeId, userId }, cancellationToken: ct));
    }

    public async Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
SELECT EXISTS (
    SELECT 1
    FROM ged.document_classification c
    WHERE c.tenant_id = @tenantId
      AND c.document_id = @documentId
      AND c.document_type_id IS NOT NULL
);
";
        var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
    }

    public async Task SetSuggestionAsync(
        Guid tenantId,
        Guid documentId,
        Guid? suggestedTypeId,
        decimal? confidence,
        string? method,
        DateTimeOffset? suggestedAt,
        CancellationToken ct)
    {
        // Guarda sugestão na própria tabela de classification (campos opcionais)
        // Caso você NÃO tenha suggested_type_id etc., a gente cria ou altera para usar colunas existentes.
        const string sql = @"
INSERT INTO ged.document_classification
(
    tenant_id, document_id,
    suggested_type_id, confidence, method, suggested_at,
    reg_date, reg_status
)
VALUES
(
    @tenantId, @documentId,
    @suggestedTypeId, @confidence, @method, @suggestedAt,
    NOW(), 'A'
)
ON CONFLICT (tenant_id, document_id)
DO UPDATE SET
    suggested_type_id = EXCLUDED.suggested_type_id,
    confidence = EXCLUDED.confidence,
    method = EXCLUDED.method,
    suggested_at = EXCLUDED.suggested_at;
";
        var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new
            {
                tenantId,
                documentId,
                suggestedTypeId,
                confidence,
                method = string.IsNullOrWhiteSpace(method) ? "RULES" : method,
                suggestedAt = suggestedAt ?? DateTimeOffset.UtcNow
            },
            cancellationToken: ct));
    }

    public async Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        // Documentos ativos sem document_type_id definido
        // (assumindo reg_status em document e classification)
        const string sql = @"
SELECT COUNT(*)
FROM ged.document d
LEFT JOIN ged.document_classification c
  ON c.tenant_id = d.tenant_id
 AND c.document_id = d.id
WHERE d.tenant_id = @tenantId
  AND d.reg_status = 'A'
  AND (@folderId IS NULL OR d.folder_id = @folderId)
  AND (c.document_type_id IS NULL);
";
        var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
    }
}
