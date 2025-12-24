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
        public int Level { get; set; }
    }
}
