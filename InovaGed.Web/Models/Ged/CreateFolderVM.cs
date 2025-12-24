namespace InovaGed.Web.Models.Ged;

public sealed class CreateFolderVM
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
}
