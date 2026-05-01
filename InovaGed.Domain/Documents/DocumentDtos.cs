using System;
using System.Collections.Generic;

namespace InovaGed.Domain.Documents;

public sealed class DocumentDetailsDto
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string Code { get; init; } = "";

    public string Title { get; init; } = "";

    public string? Description { get; init; }

    public Guid? FolderId { get; init; }

    public Guid? TypeId { get; init; }

    public Guid? ClassificationId { get; init; }

    public DocumentStatus Status { get; init; }

    public string Visibility { get; init; } = "";

    public Guid? CurrentVersionId { get; init; }

    public DateTime CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public Guid? UpdatedBy { get; init; }

    public int CurrentVersion { get; set; }

    public bool IsConfidential =>
        string.Equals(Visibility, "CONFIDENTIAL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Visibility, "Confidential", StringComparison.OrdinalIgnoreCase);
}

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