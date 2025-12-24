using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Documents;

public sealed class DocumentMetadataValue : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public Guid DocumentTypeFieldId { get; private set; }

    public string Value { get; private set; } = default!; // armazenar normalizado como string

    private DocumentMetadataValue() { }

    public DocumentMetadataValue(Guid tenantId, Guid documentId, Guid fieldId, string value, Guid createdBy)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        DocumentTypeFieldId = fieldId;
        Value = value;
        CreatedBy = createdBy;
    }

    public void SetValue(string value, Guid userId)
    {
        Value = value;
        Touch(userId);
    }
}
