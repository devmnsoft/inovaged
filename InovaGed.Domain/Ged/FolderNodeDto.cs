namespace InovaGed.Domain.Ged
{
    public sealed class FolderNodeDto
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string? Name { get; set; } = "";
        public string? Path { get; set; }
        public int Level { get; set; }
        public Guid UploadFolderId { get; set; }
        public bool IsVirtual { get; set; }
        public bool CanReceiveDocuments { get; set; }
    }

}
