namespace InovaGed.Web.Models.HospitalTrends;

public sealed class HospitalTrendsFilterVM
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public DateTime? CompareFrom { get; set; }
    public DateTime? CompareTo { get; set; }
    public Guid? FolderId { get; set; }
    public string? Sector { get; set; }
    public string? DocumentType { get; set; }
    public string? Category { get; set; }
    public int Top { get; set; } = 1000;
    public bool RefreshCache { get; set; }
}
