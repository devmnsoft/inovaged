namespace InovaGed.Application.Classification;

public sealed class DocumentTypeRowDto
{
    public DocumentTypeRowDto() { }
    public DocumentTypeRowDto(Guid Id, string Name)
    {
        this.Id = Id;
        this.Name = Name;
    }
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
