namespace InovaGed.Application.Ged.Folders;

public interface IFolderNavigationResolver
{
    Task<FolderNavigationResolution> ResolveForListingAsync(
        Guid tenantId,
        Guid userId,
        Guid? requestedFolderId,
        bool isAdmin,
        CancellationToken ct);
}

public sealed class FolderNavigationResolution
{
    public Guid? RequestedFolderId { get; set; }
    public Guid VisualFolderId { get; set; }
    public Guid ListingFolderId { get; set; }
    public Guid UploadFolderId { get; set; }
    public string FolderName { get; set; } = "Documentos gerais";
    public bool WasVirtual { get; set; }
    public bool Success { get; set; }
}
