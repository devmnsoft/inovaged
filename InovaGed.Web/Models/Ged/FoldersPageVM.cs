namespace InovaGed.Web.Models.Ged;

public sealed class FoldersPageVM
{
    // ✅ A VIEW USA Tree → então precisamos ter Tree
    public List<FolderVM> Tree { get; set; } = new();



    // (Opcional) se você também quiser usar "Folders" em outras telas
    public List<FolderVM> Folders
    {
        get => Tree;
        set => Tree = value;
    }

    public sealed class FolderVM
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public int Level { get; set; }
        public Guid UploadFolderId { get; set; }
        public bool IsVirtual { get; set; }
        public bool CanReceiveDocuments { get; set; } = true;
    }
}
