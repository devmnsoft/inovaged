namespace InovaGed.Web.Models.Classification;

public sealed class EditClassificationVM
{
    public Guid DocumentId { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public string? TagsCsv { get; set; }
    public string? MetadataLines { get; set; }

    public List<DocumentTypeItemVM> AvailableTypes { get; set; } = new();

    public sealed class DocumentTypeItemVM
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        // ✅ REMOVIDO: Code
    }
}
