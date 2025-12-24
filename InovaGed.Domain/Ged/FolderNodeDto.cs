namespace InovaGed.Domain.Ged
{
    public sealed class FolderNodeDto
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
    }

}
