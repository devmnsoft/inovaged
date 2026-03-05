namespace InovaGed.Application.RetentionCases;

public sealed class RetentionCaseRow
{
    public Guid Id { get; set; }
    public int CaseNo { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class RetentionCaseItemRow
{
    public long Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid DocumentId { get; init; }
    public string DocCode { get; init; } = "";
    public string DocTitle { get; init; } = "";
    public string ClassificationCode { get; init; } = "";
    public string ClassificationName { get; init; } = "";
    public DateTime RetentionDueAt { get; init; }           // ou DateTimeOffset, mas seja consistente com o SQL
    public string RetentionStatus { get; init; } = "";
    public string SuggestedDestination { get; init; } = "";
    public string Decision { get; init; } = "";
    public string? DecisionNotes { get; init; }
    public DateTime? DecidedAt { get; init; }               // normalmente é nullable

    // ✅ importante pro Dapper
    public RetentionCaseItemRow() { }
}

public sealed class CreateRetentionCaseRequest
{
    public string Title { get; set; } = "Caso de Destinação";
    public string? Notes { get; set; }
    public Guid[] DocumentIds { get; set; } = Array.Empty<Guid>();
}

public sealed class DecideItemRequest
{
    public long ItemId { get; set; }
    public string Decision { get; set; } = "APPROVE"; // APPROVE | REJECT
    public string? Notes { get; set; }
}