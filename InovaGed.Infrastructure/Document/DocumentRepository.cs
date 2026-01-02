using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Domain.Documents;
using InovaGed.Domain.Ged;
using Microsoft.Extensions.Logging;

namespace InovaGed.Domain.Documents;

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<DocumentRepository> _logger;

    public DocumentRepository(
        IDbConnectionFactory factory,
        ILogger<DocumentRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<Guid> CreateDocumentAsync(Document doc, CancellationToken ct)
    {
        _logger.LogInformation(
            "CreateDocumentAsync START | Tenant={TenantId} Folder={FolderId} Doc={DocId}",
            doc.TenantId, doc.FolderId, doc.Id);

        const string sql = @"
INSERT INTO ged.document
(id, tenant_id, folder_id, document_type_id, title, description, status, is_confidential, current_version_id,
 created_at_utc, created_by)
VALUES
(@Id, @TenantId, @FolderId, @DocumentTypeId, @Title, @Description, @Status, @IsConfidential, @CurrentVersionId,
 @CreatedAtUtc, @CreatedBy);";

        try
        {
            using var conn = await _factory.OpenAsync(ct);

            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                doc.Id,
                doc.TenantId,
                doc.FolderId,
                doc.DocumentTypeId,
                doc.Title,
                doc.Description,
                Status = (int)doc.Status,
                doc.IsConfidential,
                doc.CurrentVersionId,
                doc.CreatedAtUtc,
                doc.CreatedBy
            }, cancellationToken: ct));

            _logger.LogInformation(
                "CreateDocumentAsync SUCCESS | Doc={DocId}",
                doc.Id);

            return doc.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CreateDocumentAsync ERROR | Tenant={TenantId} Doc={DocId}",
                doc.TenantId, doc.Id);
            throw;
        }
    }

    public async Task<Guid> CreateVersionAsync(DocumentVersion v, CancellationToken ct)
    {
        _logger.LogInformation(
            "CreateVersionAsync START | Tenant={TenantId} Doc={DocId} Version={VersionId}",
            v.TenantId, v.DocumentId, v.Id);

        const string sql = @"
INSERT INTO ged.document_version
(id, tenant_id, document_id, version_number, file_name, content_type, size_bytes, sha256,
 storage_provider, storage_path, notes, created_at_utc, created_by)
VALUES
(@Id, @TenantId, @DocumentId, @VersionNumber, @FileName, @ContentType, @SizeBytes, @Sha256,
 @StorageProvider, @StoragePath, @Notes, @CreatedAtUtc, @CreatedBy);";

        try
        {
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, v, cancellationToken: ct));

            _logger.LogInformation(
                "CreateVersionAsync SUCCESS | Version={VersionId}",
                v.Id);

            return v.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CreateVersionAsync ERROR | Tenant={TenantId} Doc={DocId} Version={VersionId}",
                v.TenantId, v.DocumentId, v.Id);
            throw;
        }
    }

    public async Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        _logger.LogInformation(
            "GetByIdAsync START | Tenant={TenantId} Doc={DocId}",
            tenantId, documentId);

        const string sql = @"
SELECT id, tenant_id as TenantId, folder_id as FolderId, document_type_id as DocumentTypeId,
       title, description, status, is_confidential as IsConfidential, current_version_id as CurrentVersionId,
       created_at_utc as CreatedAtUtc, created_by as CreatedBy,
       updated_at_utc as UpdatedAtUtc, updated_by as UpdatedBy,
       deleted_at_utc as DeletedAtUtc, deleted_by as DeletedBy
FROM ged.document
WHERE tenant_id=@tenantId
  AND id=@documentId
  AND deleted_at_utc IS NULL;";

        try
        {
            using var conn = await _factory.OpenAsync(ct);

            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            if (row is null)
            {
                _logger.LogWarning(
                    "GetByIdAsync NOT FOUND | Tenant={TenantId} Doc={DocId}",
                    tenantId, documentId);
                return null;
            }

            var doc = (Document)Activator.CreateInstance(typeof(Document), nonPublic: true)!;
            typeof(Document).GetProperty("Id")!.SetValue(doc, (Guid)row.id);
            typeof(Document).GetProperty("TenantId")!.SetValue(doc, (Guid)row.tenantid);
            typeof(Document).GetProperty("FolderId")!.SetValue(doc, (Guid)row.folderid);
            typeof(Document).GetProperty("DocumentTypeId")!.SetValue(doc, (Guid)row.documenttypeid);
            typeof(Document).GetProperty("Title")!.SetValue(doc, (string)row.title);
            typeof(Document).GetProperty("Description")!.SetValue(doc, (string?)row.description);
            typeof(Document).GetProperty("Status")!.SetValue(doc, (DocumentStatus)(int)row.status);
            typeof(Document).GetProperty("IsConfidential")!.SetValue(doc, (bool)row.isconfidential);
            typeof(Document).GetProperty("CurrentVersionId")!.SetValue(doc, (Guid)row.currentversionid);

            _logger.LogInformation(
                "GetByIdAsync SUCCESS | Doc={DocId}",
                documentId);

            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetByIdAsync ERROR | Tenant={TenantId} Doc={DocId}",
                tenantId, documentId);
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> ListVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        _logger.LogInformation("ListVersionsAsync START | Tenant={TenantId} Doc={DocId}", tenantId, documentId);
          
        const string sql = @"
            SELECT
                v.id              AS ""Id"",
                v.document_id     AS ""DocumentId"",
                v.version_number  AS ""VersionNumber"",
                v.file_name       AS ""FileName"",
                v.content_type    AS ""ContentType"",
                v.file_size_bytes AS ""SizeBytes"",
                v.storage_path    AS ""StoragePath"",
                v.created_at      AS ""CreatedAt"",
                v.created_by      AS ""CreatedBy"",
                (v.id = d.current_version_id) AS ""IsCurrent"" 
            FROM ged.document_version v
            JOIN ged.document d
              ON d.tenant_id = v.tenant_id
             AND d.id = v.document_id
            WHERE v.tenant_id = @tenantId
              AND v.document_id = @documentId
            ORDER BY v.version_number DESC, v.created_at DESC;";

        try
        {
            using var conn = await _factory.OpenAsync(ct);

            var rows = await conn.QueryAsync<DocumentVersionDto>(
                new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));

            var list = rows.AsList();

            _logger.LogInformation("ListVersionsAsync SUCCESS | Doc={DocId} Count={Count}", documentId, list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListVersionsAsync ERROR | Tenant={TenantId} Doc={DocId}", tenantId, documentId);
            throw;
        }
    }


    public async Task SetCurrentVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid userId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "SetCurrentVersionAsync START | Tenant={TenantId} Doc={DocId} Version={VersionId}",
            tenantId, documentId, versionId);

        const string sql = @"
UPDATE ged.document
SET current_version_id=@versionId,
    updated_at_utc=NOW(),
    updated_by=@userId
WHERE tenant_id=@tenantId
  AND id=@documentId
  AND deleted_at_utc IS NULL;";

        try
        {
            using var conn = await _factory.OpenAsync(ct);

            var rows = await conn.ExecuteAsync(new CommandDefinition(
                sql, new { tenantId, documentId, versionId, userId }, cancellationToken: ct));

            _logger.LogInformation(
                "SetCurrentVersionAsync SUCCESS | Rows={Rows}",
                rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SetCurrentVersionAsync ERROR | Tenant={TenantId} Doc={DocId} Version={VersionId}",
                tenantId, documentId, versionId);
            throw;
        }
    }

    public async Task UpdateInfoAsync(
        Guid tenantId,
        Guid documentId,
        string title,
        string? description,
        bool confidential,
        Guid userId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "UpdateInfoAsync START | Tenant={TenantId} Doc={DocId}",
            tenantId, documentId);

        const string sql = @"
UPDATE ged.document
SET title=@title,
    description=@description,
    is_confidential=@confidential,
    updated_at_utc=NOW(),
    updated_by=@userId
WHERE tenant_id=@tenantId
  AND id=@documentId
  AND deleted_at_utc IS NULL;";

        try
        {
            using var conn = await _factory.OpenAsync(ct);

            var rows = await conn.ExecuteAsync(new CommandDefinition(
                sql, new { tenantId, documentId, title, description, confidential, userId }, cancellationToken: ct));

            _logger.LogInformation(
                "UpdateInfoAsync SUCCESS | Rows={Rows}",
                rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UpdateInfoAsync ERROR | Tenant={TenantId} Doc={DocId}",
                tenantId, documentId);
            throw;
        }
    }
}
