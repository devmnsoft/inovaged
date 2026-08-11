using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Ged.Labels;

public sealed record LabelRowDto(Guid Id, string LabelCode, string Title, string LabelType, string Status,
    Guid? BoxId, string? BoxNo, string? Location, DateTime CreatedAt, DateTime? LastPrintedAt);

public sealed class LabelFormDto
{
    public Guid? Id { get; set; }
    [Required, StringLength(80)] public string LabelCode { get; set; } = "";
    [Required, StringLength(160)] public string Title { get; set; } = "";
    public string LabelType { get; set; } = "CUSTOM";
    public Guid? BoxId { get; set; }
    public Guid? LocationId { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
    public string? QrPayload { get; set; }
    [Range(10, 500)] public decimal? WidthMm { get; set; }
    [Range(10, 500)] public decimal? HeightMm { get; set; }
    public string Status { get; set; } = "ACTIVE";
}
