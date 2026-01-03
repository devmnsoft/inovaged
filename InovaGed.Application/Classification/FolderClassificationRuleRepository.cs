using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Classification;

public sealed class FolderClassificationRuleRepository : IFolderClassificationRuleRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<FolderClassificationRuleRepository> _logger;

    public FolderClassificationRuleRepository(
        IDbConnectionFactory db,
        ILogger<FolderClassificationRuleRepository> logger)
    {
        _db = db;
        _logger = logger;
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
        try
        {
            _logger.LogDebug(
                "GetDefaultTypeAsync | Tenant={TenantId} Folder={FolderId}",
                tenantId, folderId);

             var conn = await _db.OpenAsync(ct);

            return await conn.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(sql, new { tenantId, folderId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao obter tipo padrão da pasta | Tenant={TenantId} Folder={FolderId}",
                tenantId, folderId);
            throw;
        }
    }

    public async Task SetDefaultTypeAsync(
        Guid tenantId,
        Guid folderId,
        Guid? documentTypeId,
        Guid? userId,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.folder
SET default_document_type_id = @documentTypeId,
    updated_at = NOW(),
    updated_by = @userId
WHERE tenant_id = @tenantId
  AND id = @folderId
  AND reg_status = 'A';
";
        try
        {
            _logger.LogDebug(
                "SetDefaultTypeAsync | Tenant={TenantId} Folder={FolderId} Type={DocumentTypeId}",
                tenantId, folderId, documentTypeId);

           var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                new CommandDefinition(sql,
                    new { tenantId, folderId, documentTypeId, userId },
                    cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao definir tipo padrão da pasta | Tenant={TenantId} Folder={FolderId}",
                tenantId, folderId);
            throw;
        }
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
        try
        {
            _logger.LogDebug(
                "HasClassificationAsync | Tenant={TenantId} Document={DocumentId}",
                tenantId, documentId);

            var conn = await _db.OpenAsync(ct);

            return await conn.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao verificar classificação do documento | Tenant={TenantId} Document={DocumentId}",
                tenantId, documentId);
            throw;
        }
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
        try
        {
            _logger.LogDebug(
                "SetSuggestionAsync | Tenant={TenantId} Document={DocumentId} SuggestedType={SuggestedTypeId} Method={Method}",
                tenantId, documentId, suggestedTypeId, method);

             var conn = await _db.OpenAsync(ct);

            await conn.ExecuteAsync(
                new CommandDefinition(sql,
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
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao salvar sugestão de classificação | Tenant={TenantId} Document={DocumentId}",
                tenantId, documentId);
            throw;
        }
    }

    public async Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(1)
FROM ged.document d
LEFT JOIN ged.document_classification dc
  ON dc.document_id = d.id
 AND dc.tenant_id = d.tenant_id
 AND dc.reg_status = 'A'
WHERE d.tenant_id = @TenantId
  AND d.status <> 'DELETED'
  AND (@FolderId IS NULL OR d.folder_id = @FolderId)
  AND dc.document_type_id IS NULL;
";
        try
        {
            _logger.LogDebug(
                "CountUnclassifiedAsync | Tenant={TenantId} Folder={FolderId}",
                tenantId, folderId);

            var con = await _db.OpenAsync(ct);

            return await con.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    sql,
                    new { TenantId = tenantId, FolderId = folderId },
                    cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao contar documentos não classificados | Tenant={TenantId} Folder={FolderId}",
                tenantId, folderId);
            throw;
        }
    }
}
