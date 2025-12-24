using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Documents;

public sealed class Document : TenantEntity
{
    public Guid FolderId { get; private set; }
    public Guid DocumentTypeId { get; private set; }

    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }

    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public bool IsConfidential { get; private set; }

    public Guid CurrentVersionId { get; private set; }

    private Document() { }

    public Document(Guid tenantId, Guid folderId, Guid documentTypeId, string title, string? description,
        bool confidential, Guid currentVersionId, Guid createdBy)
    {
        TenantId = tenantId;
        FolderId = folderId;
        DocumentTypeId = documentTypeId;
        Title = title.Trim();
        Description = description?.Trim();
        IsConfidential = confidential;
        CurrentVersionId = currentVersionId;
        CreatedBy = createdBy;
    }

    public void UpdateInfo(string title, string? description, bool confidential, Guid userId)
    {
        Title = title.Trim();
        Description = description?.Trim();
        IsConfidential = confidential;
        Touch(userId);
    }

    public void ChangeStatus(DocumentStatus status, Guid userId)
    {
        Status = status;
        Touch(userId);
    }

    public void SetCurrentVersion(Guid versionId, Guid userId)
    {
        CurrentVersionId = versionId;
        Touch(userId);
    }
}
