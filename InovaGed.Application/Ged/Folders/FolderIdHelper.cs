namespace InovaGed.Application.Ged.Folders;

public static class FolderIdHelper
{
    private const string VirtualFolderPrefix = "f0000000-0000-0000-0000-";

    public static bool IsVirtualFolder(Guid? folderId)
    {
        if (!folderId.HasValue || folderId.Value == Guid.Empty)
        {
            return true;
        }

        var text = folderId.Value.ToString("D");

        return text.StartsWith(VirtualFolderPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRealFolder(Guid? folderId)
    {
        return folderId.HasValue
            && folderId.Value != Guid.Empty
            && !IsVirtualFolder(folderId.Value);
    }
}
