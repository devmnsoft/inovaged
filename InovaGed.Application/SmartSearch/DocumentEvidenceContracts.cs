namespace InovaGed.Application.SmartSearch;

public enum DocumentQuestionScopeKind { SearchResults, SelectedDocuments, Collection }
public enum DocumentQuestionStatus { Completed, PartialCoverage, InsufficientEvidence, Cancelled }

public sealed class DocumentEvidenceQuery
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool IsAdmin { get; set; }
    public string Question { get; set; } = string.Empty;
    public DocumentQuestionScopeKind Scope { get; set; }
    public string? SearchQuery { get; set; }
    public IReadOnlyList<Guid> DocumentIds { get; set; } = [];
    public Guid? CollectionId { get; set; }
    public int MaxDocuments { get; set; } = 20;
    public int MaxPassages { get; set; } = 12;
}

public sealed class DocumentEvidenceResponse
{
    public string Heading { get; set; } = "Trechos encontrados para sua pergunta";
    public string Message { get; set; } = string.Empty;
    public DocumentQuestionStatus Status { get; set; }
    public string ScopeLabel { get; set; } = string.Empty;
    public int AvailableDocuments { get; set; }
    public int ConsideredDocuments { get; set; }
    public bool CoveragePartial { get; set; }
    public IReadOnlyList<DocumentEvidenceSource> Sources { get; set; } = [];
    public IReadOnlyList<string> Limitations { get; set; } = [];
}

public sealed class DocumentEvidenceSource
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public int? VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool ExtractedByOcr { get; set; }
    public bool ExtractionIncomplete { get; set; }
    public IReadOnlyList<DocumentEvidencePassage> Passages { get; set; } = [];
}

public sealed class DocumentEvidencePassage
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int? Page { get; set; }
    public string LocationLabel { get; set; } = string.Empty;
    public decimal Score { get; set; }
}

public interface IDocumentEvidenceService
{
    Task<DocumentEvidenceResponse> AskAsync(DocumentEvidenceQuery query, CancellationToken ct);
}
