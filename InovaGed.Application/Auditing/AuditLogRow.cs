namespace InovaGed.Application.Auditing
{
    public sealed class AuditLogRow
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string Action { get; set; } = "CREATE"; // audit_action_enum
        public string EntityName { get; set; } = "";
        public Guid? EntityId { get; set; }
        public string? Summary { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string DetailsJson { get; internal set; }
    }
}
