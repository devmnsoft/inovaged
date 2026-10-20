using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Ged.Labels;

public sealed class LabelRowDto
{
    public LabelRowDto() { }
    public LabelRowDto(Guid Id, string LabelCode, string Title, string LabelType, string Status, Guid? BoxId, string? BoxNo, string? Location, DateTime CreatedAt, DateTime? LastPrintedAt)
    {
        this.Id = Id;
        this.LabelCode = LabelCode;
        this.Title = Title;
        this.LabelType = LabelType;
        this.Status = Status;
        this.BoxId = BoxId;
        this.BoxNo = BoxNo;
        this.Location = Location;
        this.CreatedAt = CreatedAt;
        this.LastPrintedAt = LastPrintedAt;
    }
    public Guid Id { get; set; }
    public string LabelCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string LabelType { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid? BoxId { get; set; }
    public string? BoxNo { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastPrintedAt { get; set; }
}

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
