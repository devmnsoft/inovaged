using InovaGed.Domain.Ged;

namespace InovaGed.Web.Models.Ged;

public sealed class GedDetailsVM
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool IsConfidential { get; set; }

    public Guid? CreatedBy { get; set; }
    public Guid? CurrentVersionId { get; set; }

    // ✅ SOMENTE ESSE
    public List<VersionVM> Versions { get; set; } = new();

    public sealed class VersionVM
    {
        public Guid Id { get; set; }
        public int VersionNumber { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public bool IsCurrent { get; set; }
    }
}
