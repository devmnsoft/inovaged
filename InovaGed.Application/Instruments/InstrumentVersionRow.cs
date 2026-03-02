namespace InovaGed.Application.Instruments
{
    public sealed class InstrumentVersionRow
    {
        public Guid Id { get; set; }
        public string InstrumentType { get; set; } = "";
        public int VersionNo { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public string PublishedByName { get; set; } = "";
        public string HashSha256 { get; set; } = "";
        public string? Notes { get; set; }
    }

    public sealed class InstrumentNodeRow
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public string SecurityLevel { get; set; } = "PUBLIC";
    }
}
