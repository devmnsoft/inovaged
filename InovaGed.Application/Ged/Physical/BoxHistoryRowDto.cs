namespace InovaGed.Application.Ged.Physical;

public sealed class BoxHistoryRowDto
{
    public DateTime At { get; set; }
    public string EventType { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public Guid BatchId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string? Notes { get; set; }
}