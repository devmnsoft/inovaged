namespace InovaGed.Application.Ged.Folders;

public sealed class MoveFolderTarget
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Level { get; set; }
}
