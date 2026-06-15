using InovaGed.Application.SmartSearch;

namespace InovaGed.Application.Ged.Search;

public interface IGedSmartQueryParser : ISmartQueryParser { }
public interface IGedSmartSearchRepository : ISmartSearchRepository { }


public interface IGedSmartSearchDiagnosticsService
{
    Task<GedSmartSearchDiagnosticsVm> GetAsync(Guid tenantId, CancellationToken ct);
    Task<int> EnqueueReindexDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<int> EnqueueReindexAllAsync(Guid tenantId, CancellationToken ct);
    Task<int> EnqueueReindexMissingAsync(Guid tenantId, CancellationToken ct);
    Task<int> RebuildVectorsAsync(Guid tenantId, CancellationToken ct);
}

public sealed class GedSmartSearchDiagnosticsVm
{
    public int ActiveDocuments { get; set; }
    public int IndexedDocuments { get; set; }
    public int MissingIndexDocuments { get; set; }
    public int EmptySearchTextDocuments { get; set; }
    public int NullSearchVectorDocuments { get; set; }
    public DateTimeOffset? LastIndexing { get; set; }
    public int OcrAvailable { get; set; }
    public bool HasUnaccent { get; set; }
    public bool HasPgTrgm { get; set; }
    public bool HasProcessingJob { get; set; }
    public string? SchemaWarning { get; set; }
    public IReadOnlyList<GedSmartSearchTenantCountVm> Tenants { get; set; } = Array.Empty<GedSmartSearchTenantCountVm>();
    public IReadOnlyList<GedSmartSearchJobStatusVm> SmartIndexJobs { get; set; } = Array.Empty<GedSmartSearchJobStatusVm>();
    public IReadOnlyList<GedSmartSearchMissingDocumentVm> TopMissingDocuments { get; set; } = Array.Empty<GedSmartSearchMissingDocumentVm>();
}

public sealed class GedSmartSearchTenantCountVm
{
    public Guid TenantId { get; set; }
    public int ActiveDocuments { get; set; }
    public int IndexedDocuments { get; set; }
}

public sealed class GedSmartSearchJobStatusVm
{
    public string Status { get; set; } = string.Empty;
    public int Total { get; set; }
}

public sealed class GedSmartSearchMissingDocumentVm
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FolderName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public interface IGedSearchSuggestionService
{
    Task<IReadOnlyList<SmartSearchSuggestionDto>> SuggestAsync(SmartSearchRequest request, CancellationToken ct);
}

public interface IGedSearchStatisticsService
{
    Task<global::InovaGed.Application.SmartSearch.SmartSearchStatistics> GetAsync(Guid tenantId, CancellationToken ct);
}

public interface IGedSearchIndexService
{
    Task<int> ReindexDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<int> ReindexVersionAsync(Guid tenantId, Guid documentVersionId, CancellationToken ct);
    Task<int> ReindexPendingAsync(Guid tenantId, int limit, CancellationToken ct);
    Task<int> ReindexAllAsync(Guid tenantId, CancellationToken ct);
}

public interface IGedOcrMetadataExtractor : IDocumentOcrMetadataExtractor { }

public sealed class GedSmartSearchIntent
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public string? PatientName { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? ProtocolNumber { get; set; }
    public int? Age { get; set; }
    public int? AgeFrom { get; set; }
    public int? AgeTo { get; set; }
    public int? Year { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? DocumentType { get; set; }
    public string? ExamType { get; set; }
    public List<string> Keywords { get; set; } = [];
    public List<string> ExpandedTerms { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;
}

public sealed class GedDocumentScope
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class GedSmartSearchResult
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FolderName { get; set; }
    public string? DocumentType { get; set; }
    public string? ClassificationName { get; set; }
    public decimal Score { get; set; }
    public string? OcrSnippet { get; set; }
    public List<GedSearchReason> Reasons { get; set; } = [];
}

public sealed class GedSearchReason
{
    public string Reason { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public int Weight { get; set; }
}
