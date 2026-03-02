namespace InovaGed.Application.RetentionCases;

public sealed class RetentionCaseRow
{
    public Guid Id { get; set; }
    public int CaseNo { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record RetentionCaseItemRow(
    long Id,
    Guid CaseId,
    Guid DocumentId,
    string? DocCode,
    string? DocTitle,
    string? ClassificationCode,
    string? ClassificationName,
    DateTimeOffset? RetentionDueAt,
    string? RetentionStatus,
    string? SuggestedDestination,
    string Decision,
    string? DecisionNotes,
    DateTimeOffset? DecidedAt
);

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