namespace InovaGed.Web.ViewModels;

public sealed class BoxContentsVm
{
    public Guid? BoxId { get; set; }
    public BoxHeader? Box { get; set; }
    public List<ItemRow> Items { get; set; } = new();

    public sealed class BoxHeader
    {
        public Guid Id { get; set; }
        public string LabelCode { get; set; } = "";
        public string? Notes { get; set; }
        public Guid? LocationId { get; set; }
    }

    public sealed class ItemRow
    {
        public Guid BatchItemId { get; set; }
        public Guid BatchId { get; set; }
        public string BatchNo { get; set; } = "";
        public Guid DocumentId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset? LinkedAt { get; set; }
    }
}