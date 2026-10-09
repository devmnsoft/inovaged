namespace InovaGed.Web.Models.Classification;

public sealed class EditClassificationVM
{
    public Guid DocumentId { get; set; }
    public Guid? ClassificationId { get; set; }
    public string? ClassificationLabel { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public string? TagsCsv { get; set; }
    public string? MetadataLines { get; set; }

    public List<ClassificationPlanItem> AvailableClassifications { get; set; } = new();
    public List<DocumentTypeItem> AvailableTypes { get; set; } = new();

    public sealed class ClassificationPlanItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";
    }

    public sealed class DocumentTypeItemVM
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        // ✅ REMOVIDO: Code
    }

    public sealed class DocumentTypeItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        // ✅ REMOVIDO: Code
    }
}
