namespace InovaGed.Application.SmartSearch;

public sealed class SmartSearchRequest
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Query { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? DocumentType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public bool IncludeOcr { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;
    public bool IncludeStatistics { get; set; } = true;
    public bool IsAdmin { get; set; }
    public string? Source { get; set; } = "SMART_SEARCH";
}

public sealed class SmartSearchIntent
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string? PatientName { get; set; }
    public string? PersonName { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? ProtocolNumber { get; set; }
    public int? Age { get; set; }
    public int? AgeFrom { get; set; }
    public int? AgeTo { get; set; }
    public string? DiseaseTerm { get; set; }
    public List<string> ClinicalTerms { get; set; } = [];
    public List<string> ExpandedTerms { get; set; } = [];
    public string? DocumentType { get; set; }
    public string? ExamType { get; set; }
    public int? Year { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool IsApproxDate { get; set; }
    public List<string> Keywords { get; set; } = [];
    public string? SearchMode { get; set; } = "Expanded";
    public string Explanation { get; set; } = string.Empty;
    public string ExpandedQuery { get; set; } = string.Empty;
}

public sealed class SmartSearchResult
{
    public SmartSearchIntent Intent { get; set; } = new();
    public IReadOnlyList<SmartSearchResultItem> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages { get; set; }
    public long DurationMs { get; set; }
    public string? Warning { get; set; }
    public IReadOnlyList<string> Tokens { get; set; } = [];
    public bool SearchedDirect { get; set; }
    public bool SearchedIndex { get; set; }
    public bool SearchedOcr { get; set; }
    public bool IndexAvailable { get; set; }
    public int FallbackCount { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<string> Suggestions { get; set; } = [];
}

public sealed class SmartSearchResultItem
{
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FolderName { get; set; }
    public string? DocumentType { get; set; }
    public string? Classification { get; set; }
    public string? ClassificationName { get; set; }
    public string? PatientName { get; set; }
    public int? Age { get; set; }
    public int? Year { get; set; }
    public string? OcrSnippet { get; set; }
    public decimal Score { get; set; }
    public bool HasOcr { get; set; }
    public IReadOnlyList<SmartSearchResultReason> Reasons { get; set; } = [];
}

public sealed class SmartSearchResultReason
{
    public string Reason { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public int Weight { get; set; }
}

public sealed class DocumentQuestionRequest
{
    public Guid DocumentId { get; set; }
    public string Question { get; set; } = string.Empty;
}

public sealed class DocumentQuestionAnswer
{
    public string Answer { get; set; } = string.Empty;
    public List<string> EvidenceSnippets { get; set; } = [];
    public bool FoundInDocument { get; set; }
    public string SafetyNotice { get; set; } = "Resposta documental baseada exclusivamente no OCR/metadados disponíveis; não constitui diagnóstico médico.";
}

public sealed class SmartSearchSuggestion
{
    public string Text { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public sealed class SmartSearchStatistics
{
    public int SearchesToday { get; set; }
    public int SearchesWithoutResult { get; set; }
    public decimal OcrAvailablePercent { get; set; }
    public decimal OcrMissingPercent { get; set; }
    public int AverageDurationMs { get; set; }
    public IReadOnlyList<KeyValuePair<string, int>> TopTerms { get; set; } = [];
    public IReadOnlyList<KeyValuePair<string, int>> TopDocumentTypes { get; set; } = [];
    public IReadOnlyList<KeyValuePair<string, int>> MostAccessedDocuments { get; set; } = [];
}

public sealed class UserDocumentScope
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public bool IsAdmin { get; set; }
}
