using System;

namespace InovaGed.Domain.Documents;
   
public sealed class DocumentDetailsDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public Guid FolderId { get; init; }
    public Guid? TypeId { get; init; }
    public Guid? ClassificationId { get; init; }
    public string Status { get; init; } = "";
    public string Visibility { get; init; } = "";
    public Guid? CurrentVersionId { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid? UpdatedBy { get; init; }
    public bool IsConfidential { get; set; }
    public int CurrentVersion { get; set; }



    public List<VersionVM> Versions { get; set; } = new();

}
public sealed class VersionVM
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsCurrent { get; set; }
}  
 
