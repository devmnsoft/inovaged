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


}
