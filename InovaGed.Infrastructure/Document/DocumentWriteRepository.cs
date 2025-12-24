using System.Data;
using Dapper;
using InovaGed.Application.Documents;
using InovaGed.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Documents;

public sealed class DocumentWriteRepository : IDocumentWriteRepository
{
    private readonly ILogger<DocumentWriteRepository> _logger;

    public DocumentWriteRepository(ILogger<DocumentWriteRepository> logger)
    {
        _logger = logger;
    }

    public async Task InsertDocumentAsync(DocumentRow row, IDbTransaction tx, CancellationToken ct)
    {

        const string sql = @"
        INSERT INTO ged.document (
            id, tenant_id, code, title, description,
            folder_id, department_id, type_id, classification_id,
            status, visibility, current_version_id,
            created_at, created_by
        ) VALUES (
            @Id, @TenantId, @Code, @Title, @Description,
            @FolderId, @DepartmentId, @TypeId, @ClassificationId,
            @Status::ged.document_status_enum,
            @Visibility::ged.document_visibility_enum,
            @CurrentVersionId,
            NOW(), @CreatedBy
        );";



        try
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, row, tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao inserir document. Id={Id}", row.Id);
            throw;
        }
    }

    public async Task<int> GetNextVersionNumberAsync(Guid tenantId, Guid documentId, IDbTransaction tx, CancellationToken ct)
    {
        // ✅ Schema correto: ged.document_version
        const string sql = @"
SELECT COALESCE(MAX(version_number), 0) + 1
FROM ged.document_version
WHERE tenant_id = @tenantId
  AND document_id = @documentId;";

        try
        {
            return await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
                sql, new { tenantId, documentId }, tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao calcular próxima versão. Doc={DocId}", documentId);
            throw;
        }
    }

    public async Task InsertVersionAsync(DocumentVersionRow row, IDbTransaction tx, CancellationToken ct)
    {
        // ✅ Schema correto: ged.document_version
        const string sql = @"
INSERT INTO ged.document_version (
    id, tenant_id, document_id, version_number,
    file_name, file_extension, file_size_bytes,
    storage_path, checksum_md5, checksum_sha256,
    content_type,
    created_by
) VALUES (
    @Id, @TenantId, @DocumentId, @VersionNumber,
    @FileName, @FileExtension, @FileSizeBytes,
    @StoragePath, @ChecksumMd5, @ChecksumSha256,
    @ContentType,
    @CreatedBy
);";

        try
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(sql, row, tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao inserir document_version. VersionId={Id}", row.Id);
            throw;
        }
    }

    public async Task UpdateCurrentVersionAsync(Guid tenantId, Guid documentId, Guid currentVersionId, Guid? userId, IDbTransaction tx, CancellationToken ct)
    {
        const string sql = @"
UPDATE ged.document
SET current_version_id = @currentVersionId
WHERE tenant_id = @tenantId
  AND id = @documentId;";


        try
        {
            await tx.Connection!.ExecuteAsync(new CommandDefinition(sql,
                new { tenantId, documentId, currentVersionId, userId }, tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar current_version_id. Doc={DocId}", documentId);
            throw;
        }
    }
}
