namespace InovaGed.Application.RetentionTerms;

public sealed class RetentionTermRow
{
    public RetentionTermRow() { }

    public Guid Id { get; set; }
    public int TermNo { get; set; }
    public Guid CaseId { get; set; }
    public string TermType { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateTermRequest
{
    public Guid CaseId { get; set; }
    public string TermType { get; set; } = "ELIMINATION"; // ELIMINATION|TRANSFER|COLLECTION
    public string? Notes { get; set; }
}

public sealed class SignTermRequest
{
    public Guid TermId { get; set; }
    public string SignerName { get; set; } = "";
    public string? SignerRole { get; set; }
    public string? SignerDocument { get; set; }
}

public interface IRetentionTermRepository
{
    // ✅ NOVO: sobrecarga com filtros (é isso que seu Controller quer usar)
    Task<IReadOnlyList<RetentionTermRow>> ListAsync(
        Guid tenantId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? status,
        CancellationToken ct);
    Task<IReadOnlyList<RetentionTermRow>> ListAsync(Guid tenantId, CancellationToken ct);
    Task<(RetentionTermRow Term, string Html)?> GetAsync(Guid tenantId, Guid termId, CancellationToken ct);

    Task<Guid> CreateFromCaseAsync(Guid tenantId, Guid userId, CreateTermRequest req, CancellationToken ct);
    Task MarkReadyToSignAsync(Guid tenantId, Guid userId, Guid termId, CancellationToken ct);

    Task SignAsync(Guid tenantId, Guid userId, SignTermRequest req, CancellationToken ct);

    Task ExecuteFinalAsync(Guid tenantId, Guid userId, Guid termId, CancellationToken ct);
}