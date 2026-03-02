using InovaGed.Application.Common.Paging;

namespace InovaGed.Application.Reports;
 

public sealed class DispositionKpiVM
{
    public long EliminationReady { get; }
    public long TransferReady { get; }
    public long ReviewRequired { get; }
    public long DisposedLast30D { get; }

    public DispositionKpiVM(long eliminationready, long transferready, long reviewrequired, long disposedlast30d)
    {
        EliminationReady = eliminationready;
        TransferReady = transferready;
        ReviewRequired = reviewrequired;
        DisposedLast30D = disposedlast30d;
    }
}

public sealed class DispositionFilter
{
    public string? Status { get; set; } // ELIMINATION_READY|TRANSFER_READY|REVIEW_REQUIRED|DISPOSED
    public string? Q { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public sealed class DispositionRowVM
{
    public Guid DocumentId { get; set; }
    public string? DocCode { get; set; }
    public string? DocTitle { get; set; }

    public string? DispositionStatus { get; set; }
    public DateTime? DispositionAt { get; set; }

    public Guid? CaseId { get; set; }

    public string? ClassCode { get; set; }
    public string? ClassName { get; set; }

    public DateTime? RetentionDueAt { get; set; }
    public string? RetentionStatus { get; set; }
}

public sealed class TermRowVM
{
    public Guid TermId { get; set; }
    public int TermNo { get; set; }
    public Guid CaseId { get; set; }

    public string? TermType { get; set; }
    public string? Status { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? SignedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
}

public interface IDispositionReportsQueries
{
    Task<DispositionKpiVM> GetKpisAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<DispositionRowVM>> ListDispositionAsync(Guid tenantId, DispositionFilter f, CancellationToken ct);
    Task<IReadOnlyList<TermRowVM>> ListTermsAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, string? status, CancellationToken ct);

    Task<PagedResult<DispositionRowVM>> ListDispositionPagedAsync(Guid tenantId, DispositionFilter f, int page, int pageSize, CancellationToken ct);
}