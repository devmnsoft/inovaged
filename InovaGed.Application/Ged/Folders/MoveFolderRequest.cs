namespace InovaGed.Application.Ged.Folders;

public sealed class MoveFolderRequest
{
    public Guid FolderId { get; set; }
    public Guid? DestinationParentId { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
