using System;
using System.IO;

namespace InovaGed.Application.Documents;

public sealed class UploadDocumentCommand
{
    public Guid FolderId { get; init; }
    public Guid? TypeId { get; init; }            // document.type_id
    public Guid? DepartmentId { get; init; }      // document.department_id
    public Guid? ClassificationId { get; init; }  // document.classification_id

    public string Title { get; init; } = "";
    public string? Description { get; init; }

    // ✅ No teu banco: document_visibility_enum = PRIVATE | INTERNAL | PUBLIC
    public string Visibility { get; init; } = "INTERNAL";

    public bool? IsDeleted { get; init; }

    public bool? IsConfidential { get; init; }
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "application/octet-stream";
    public Stream Content { get; init; } = Stream.Null;
    public DateTime UploadedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsPartialDocument { get; init; }
    public bool IsDocumentIncomplete { get; init; }
    public string? IncompleteReason { get; init; }
    public int? PartNumber { get; init; }
    public int? TotalParts { get; init; }
    public Guid? PartialGroupId { get; init; }
    public int? PartialPartNumber { get; init; }
    public int? PartialTotalParts { get; init; }
    public string PartialStatus { get; init; } = "NOT_PARTIAL";
    public Guid? ConsolidatedVersionId { get; init; }


}
