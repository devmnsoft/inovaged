using System.ComponentModel.DataAnnotations;

namespace InovaGed.Application.Pacs;

public sealed class NewPacsTicketVM
{
    [Required]
    public string FolderName { get; set; } = "";

    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    [StringLength(120)]
    public string? PatientName { get; set; }

    [StringLength(50)]
    public string? PatientId { get; set; }

    [StringLength(100)]
    public string? ExamType { get; set; }

    public DateTime? ExamDate { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public sealed class PacsInboundFolderVM
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
}

public sealed class PacsInboundFileVM
{
    public string Name { get; set; } = "";
    public string Ext { get; set; } = "";
    public long SizeBytes { get; set; }
}