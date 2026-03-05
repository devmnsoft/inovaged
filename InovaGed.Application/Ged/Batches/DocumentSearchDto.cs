namespace InovaGed.Application.Ged.Batches;

public sealed class DocumentSearchDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
}