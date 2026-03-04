namespace InovaGed.Application.Audit
{
    public sealed class AuditIndexVM
    {
        public AuditSearchVM Search { get; set; } = new();
        public IReadOnlyList<AuditLogRowDto> Rows { get; set; } = Array.Empty<AuditLogRowDto>();

        public int Total { get; set; }
        public int TotalPages { get; set; }
    }
}
