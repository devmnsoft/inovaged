using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Documents;

public sealed class DocumentTypeField : TenantEntity
{
    public Guid DocumentTypeId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Key { get; private set; } = default!;
    public MetadataFieldType FieldType { get; private set; }
    public bool IsRequired { get; private set; }
    public int SortOrder { get; private set; }

    private DocumentTypeField() { }

    public DocumentTypeField(Guid tenantId, Guid documentTypeId, string name, string key, MetadataFieldType fieldType,
        bool isRequired, int sortOrder, Guid createdBy)
    {
        TenantId = tenantId;
        DocumentTypeId = documentTypeId;
        Name = name.Trim();
        Key = key.Trim();
        FieldType = fieldType;
        IsRequired = isRequired;
        SortOrder = sortOrder;
        CreatedBy = createdBy;
    }
}
