namespace InovaGed.Application.Audit
{
    public sealed class AuditSearchVM
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public string? Q { get; set; }
        public string? EventType { get; set; }
        public string? Action { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
