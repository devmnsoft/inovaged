namespace InovaGed.Application.Ged.Folders;

public interface IUploadFolderResolver
{
    Task<UploadFolderResolutionResult> ResolveAsync(
        Guid tenantId,
        Guid userId,
        Guid folderId,
        bool isAdmin,
        CancellationToken ct);
}

public sealed class UploadFolderResolutionResult
{
    public bool Success { get; set; }
    public Guid RequestedFolderId { get; set; }
    public Guid ResolvedFolderId { get; set; }
    public bool WasVirtual { get; set; }
    public bool CreatedRealFolder { get; set; }
    public string? FolderName { get; set; }
    public string? FolderPath { get; set; }
    public string? Message { get; set; }

    public static UploadFolderResolutionResult Fail(Guid requestedFolderId, string message) => new()
    {
        Success = false,
        RequestedFolderId = requestedFolderId,
        Message = message
    };
}
