using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Documents;

public sealed class DocumentVersion : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }

    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = default!;

    public string StorageProvider { get; private set; } = "FILESYSTEM";
    public string StoragePath { get; private set; } = default!;

    public string? Notes { get; private set; }

    public Guid Id { get; set; }
    
    public string StorageKey { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? createdBy { get; set; }
    

    private DocumentVersion() { }

    public DocumentVersion(Guid tenantId, Guid documentId, int versionNumber,
        string fileName, string contentType, long sizeBytes, string sha256,
        string storagePath, string? notes, Guid createdBy)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        VersionNumber = versionNumber;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StoragePath = storagePath;
        Notes = notes;
        CreatedBy = createdBy;
    }
}
