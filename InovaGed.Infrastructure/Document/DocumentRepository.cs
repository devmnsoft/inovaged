using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents; 
namespace InovaGed.Domain.Documents; 

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly IDbConnectionFactory _factory;

    public DocumentRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateDocumentAsync(Document doc, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO ged.document
            (id, tenant_id, folder_id, document_type_id, title, description, status, is_confidential, current_version_id,
             created_at_utc, created_by)
            VALUES
            (@Id, @TenantId, @FolderId, @DocumentTypeId, @Title, @Description, @Status, @IsConfidential, @CurrentVersionId,
             @CreatedAtUtc, @CreatedBy);";

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

        return doc.Id;
    }

    public async Task<Guid> CreateVersionAsync(DocumentVersion v, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO ged.document_version
            (id, tenant_id, document_id, version_number, file_name, content_type, size_bytes, sha256,
             storage_provider, storage_path, notes, created_at_utc, created_by)
            VALUES
            (@Id, @TenantId, @DocumentId, @VersionNumber, @FileName, @ContentType, @SizeBytes, @Sha256,
             @StorageProvider, @StoragePath, @Notes, @CreatedAtUtc, @CreatedBy);";

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, v, cancellationToken: ct));
        return v.Id;
    }

    public async Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, tenant_id as TenantId, folder_id as FolderId, document_type_id as DocumentTypeId,
                   title, description, status, is_confidential as IsConfidential, current_version_id as CurrentVersionId,
                   created_at_utc as CreatedAtUtc, created_by as CreatedBy,
                   updated_at_utc as UpdatedAtUtc, updated_by as UpdatedBy,
                   deleted_at_utc as DeletedAtUtc, deleted_by as DeletedBy
            FROM ged.document
            WHERE tenant_id=@tenantId AND id=@documentId AND deleted_at_utc IS NULL;";

        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        if (row is null) return null;

        // v1: mapeamento manual (depois você pode usar um mapper)
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
        return doc;
    }

    public async Task<IReadOnlyList<DocumentVersion>> ListVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, tenant_id as TenantId, document_id as DocumentId, version_number as VersionNumber,
                   file_name as FileName, content_type as ContentType, size_bytes as SizeBytes, sha256 as Sha256,
                   storage_provider as StorageProvider, storage_path as StoragePath, notes,
                   created_at_utc as CreatedAtUtc, created_by as CreatedBy
            FROM ged.document_version
            WHERE tenant_id=@tenantId AND document_id=@documentId AND deleted_at_utc IS NULL
            ORDER BY version_number DESC;";

        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<DocumentVersion>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetCurrentVersionAsync(Guid tenantId, Guid documentId, Guid versionId, Guid userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ged.document
            SET current_version_id=@versionId, updated_at_utc=NOW(), updated_by=@userId
            WHERE tenant_id=@tenantId AND id=@documentId AND deleted_at_utc IS NULL;";

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, versionId, userId }, cancellationToken: ct));
    }

    public async Task UpdateInfoAsync(Guid tenantId, Guid documentId, string title, string? description, bool confidential, Guid userId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE ged.document
            SET title=@title, description=@description, is_confidential=@confidential,
                updated_at_utc=NOW(), updated_by=@userId
            WHERE tenant_id=@tenantId AND id=@documentId AND deleted_at_utc IS NULL;";

        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, documentId, title, description, confidential, userId }, cancellationToken: ct));
    }
}
