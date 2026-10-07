namespace InovaGed.Application.Ged.Documents;

/// <summary>Status: APPLIED, PENDING (classification committed, recalculation durable and queued), DENIED or FAILED. Code is a sanitized operational code.</summary>
public sealed record DocumentBulkClassificationItem(Guid DocumentId, bool Success, string Message, string Status = "APPLIED", string Code = "APPLIED");
public sealed record DocumentBulkClassificationResult(int Requested, int Succeeded, int Failed,
    IReadOnlyList<DocumentBulkClassificationItem> Items)
{
    public int Applied => Items.Count(x => x.Status == "APPLIED");
    public int Pending => Items.Count(x => x.Status == "PENDING");
    public int Denied => Items.Count(x => x.Status == "DENIED");
    public string AuditStatus { get; init; } = "RECORDED";
}

public interface IDocumentBulkClassificationService
{
    Task<DocumentBulkClassificationResult> ApplyAsync(Guid tenantId, Guid userId,
        IReadOnlyCollection<Guid> documentIds, Guid classificationId, CancellationToken ct);
}
