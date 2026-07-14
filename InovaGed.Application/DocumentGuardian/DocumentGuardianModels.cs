namespace InovaGed.Application.DocumentGuardian;

public sealed class DocumentGuardianViewModel
{
    public Guid DocumentId { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FolderName { get; set; }
    public string? DocumentTypeName { get; set; }
    public bool IsConfidential { get; set; }
    public decimal CompletenessScore { get; set; }
    public decimal RiskScore { get; set; }
    public IReadOnlyList<DocumentGuardianFindingDto> Findings { get; set; } = Array.Empty<DocumentGuardianFindingDto>();
    public IReadOnlyList<DocumentGuardianRelationshipDto> Relationships { get; set; } = Array.Empty<DocumentGuardianRelationshipDto>();
    public IReadOnlyList<DocumentGuardianTimelineEventDto> Timeline { get; set; } = Array.Empty<DocumentGuardianTimelineEventDto>();
    public IReadOnlyList<DocumentGuardianObligationDto> Obligations { get; set; } = Array.Empty<DocumentGuardianObligationDto>();
    public IReadOnlyList<DocumentGuardianDecisionDto> Decisions { get; set; } = Array.Empty<DocumentGuardianDecisionDto>();
}

public sealed class DocumentGuardianFindingDto
{
    public Guid Id { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public int RuleVersion { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public IReadOnlyList<DocumentGuardianEvidenceDto> Evidences { get; set; } = Array.Empty<DocumentGuardianEvidenceDto>();
}

public class DocumentGuardianEvidenceDto
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string EvidenceKey { get; set; } = string.Empty;
    public string? EvidenceValue { get; set; }
    public string? Excerpt { get; set; }
    public decimal Confidence { get; set; }
}

public sealed class DocumentGuardianRelationshipDto { public Guid RelatedDocumentId { get; set; } public string RelationshipType { get; set; } = string.Empty; public string? RelatedTitle { get; set; } public decimal Confidence { get; set; } }
public sealed class DocumentGuardianTimelineEventDto { public DateTime EventAtUtc { get; set; } public string EventType { get; set; } = string.Empty; public string Source { get; set; } = string.Empty; public string? Summary { get; set; } }
public sealed class DocumentGuardianObligationDto { public Guid Id { get; set; } public string ObligationType { get; set; } = string.Empty; public DateTime? DueAtUtc { get; set; } public string Status { get; set; } = string.Empty; public string? Description { get; set; } }
public sealed class DocumentGuardianDecisionDto { public Guid Id { get; set; } public Guid FindingId { get; set; } public string Decision { get; set; } = string.Empty; public string Justification { get; set; } = string.Empty; public DateTime DecidedAtUtc { get; set; } public string? DecidedByName { get; set; } }
