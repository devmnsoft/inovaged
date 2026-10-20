namespace InovaGed.Application.Audit
{
    public sealed class AuditLogRowDto
    {
        public AuditLogRowDto() { }
        public AuditLogRowDto(long Id, DateTime EventTime, string EventType, Guid? UserId, string? UserName, string? UserEmail, string Action, string EntityName, Guid? EntityId, string? Summary, string? IpAddress)
        {
            this.Id = Id;
            this.EventTime = EventTime;
            this.EventType = EventType;
            this.UserId = UserId;
            this.UserName = UserName;
            this.UserEmail = UserEmail;
            this.Action = Action;
            this.EntityName = EntityName;
            this.EntityId = EntityId;
            this.Summary = Summary;
            this.IpAddress = IpAddress;
        }
        public long Id { get; set; }
        public DateTime EventTime { get; set; }
        public string EventType { get; set; } = "";
        public Guid? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string Action { get; set; } = "";
        public string EntityName { get; set; } = "";
        public Guid? EntityId { get; set; }
        public string? Summary { get; set; }
        public string? IpAddress { get; set; }
    }
}
