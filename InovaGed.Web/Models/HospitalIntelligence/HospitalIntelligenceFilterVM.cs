namespace InovaGed.Web.Models.HospitalIntelligence;
public sealed class HospitalIntelligenceFilterVM
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? FolderId { get; set; }
    public string? Sector { get; set; }
    public string? DocumentType { get; set; }
    public string? Search { get; set; }
    public int Top { get; set; } = 10;
    public bool Refresh { get; set; }
}
