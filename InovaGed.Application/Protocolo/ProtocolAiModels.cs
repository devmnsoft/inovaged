using System.Text.Json.Serialization;

namespace InovaGed.Application.Protocolo;

public enum ProtocolAiTaskKind
{
    All,
    SummarizeProcess,
    DocumentPending,
    SuggestSubject,
    PrepareDispatchDraft
}

public sealed class ProtocolAiAssistRequest
{
    public Guid ProtocoloId { get; set; }
    public string? TaskKind { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class ProtocolAiSourceDto
{
    public Guid? DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public bool HasOcr { get; set; }
    public string? Evidence { get; set; }
}

public sealed class ProtocolAiCoverageDto
{
    public bool Partial { get; set; }
    public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();
    public int TotalSources { get; set; }
    public int ProcessedSources { get; set; }
}

public sealed class ProtocolAiPendingItemDto
{
    public string Item { get; set; } = string.Empty;
    public string Status { get; set; } = "CONFERENCIA_HUMANA"; // CONFIRMADO ou CONFERENCIA_HUMANA
    public string? Evidence { get; set; }
    public bool RequiresHumanCheck { get; set; } = true;
}

public sealed class ProtocolAiAssistResultDto
{
    public bool Success { get; set; }
    public Guid ExecutionId { get; set; }
    public string State { get; set; } = "Completed";
    public string CorrelationId { get; set; } = string.Empty;
    public bool ReviewRequired { get; set; } = true;
    public long ConcurrencyToken { get; set; }
    public string ProtocolNumber { get; set; } = string.Empty;
    public string CurrentSubject { get; set; } = string.Empty;
    public string? CurrentSector { get; set; }
    public string? CurrentStatus { get; set; }
    public string? CurrentDescription { get; set; }
    public string? Summary { get; set; }
    public IReadOnlyList<ProtocolAiPendingItemDto> PendingItems { get; set; } = Array.Empty<ProtocolAiPendingItemDto>();
    public string? SuggestedSubject { get; set; }
    public string? SuggestedDescription { get; set; }
    public string? DispatchDraft { get; set; }
    public IReadOnlyList<ProtocolAiSourceDto> Sources { get; set; } = Array.Empty<ProtocolAiSourceDto>();
    public ProtocolAiCoverageDto Coverage { get; set; } = new();
    public IReadOnlyList<string> Limitations { get; set; } = Array.Empty<string>();
    public string? ErrorMessage { get; set; }
}

public sealed class ProtocolAiApplySubjectRequest
{
    public Guid ProtocoloId { get; set; }
    public Guid ExecutionId { get; set; }
    public long ConcurrencyToken { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Accepted { get; set; } = true;
    public string? Notes { get; set; }
}

public sealed class ProtocolAiApplyDraftRequest
{
    public Guid ProtocoloId { get; set; }
    public Guid ExecutionId { get; set; }
    public long ConcurrencyToken { get; set; }
    public string DraftText { get; set; } = string.Empty;
    public bool Accepted { get; set; } = true;
    public string? Notes { get; set; }
}

public sealed class ProtocolAiApplyResultDto
{
    public bool Success { get; set; }
    public bool AlreadyApplied { get; set; }
    public long ConcurrencyToken { get; set; }
    public Guid? RevisionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AppliedContent { get; set; }
}

public sealed class ProtocolAiRevisionRowDto
{
    public Guid Id { get; set; }
    public Guid ProtocoloId { get; set; }
    public Guid ExecutionId { get; set; }
    public string Task { get; set; } = string.Empty;
    public string DecisionType { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; }
    public string? AppliedContent { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProtocolAiHistoryDto
{
    public bool Success { get; set; }
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<ProtocolAiRevisionRowDto> Items { get; set; } = Array.Empty<ProtocolAiRevisionRowDto>();
}
