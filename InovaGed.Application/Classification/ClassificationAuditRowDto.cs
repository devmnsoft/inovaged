namespace InovaGed.Application.Classification;

public sealed class ClassificationAuditRowDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }

    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? Method { get; set; }

    public string? BeforeJson { get; set; }  // jsonb -> string (ou JsonDocument)
    public string? AfterJson { get; set; }

    public string? Source { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
