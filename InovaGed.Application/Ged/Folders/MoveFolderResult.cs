namespace InovaGed.Application.Ged.Folders;

public sealed class MoveFolderResult
{
    public Guid FolderId { get; set; }
    public Guid? OldParentId { get; set; }
    public Guid? NewParentId { get; set; }
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
}
