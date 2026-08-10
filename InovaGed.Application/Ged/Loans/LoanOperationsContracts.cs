using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public interface ILoanOverdueService
{
    Task<int> RunAsync(Guid tenantId, Guid? actorId, CancellationToken ct);
}

public interface ILoanCollectionService
{
    Task<Result> CollectAsync(Guid tenantId, Guid loanId, Guid? actorId, string? message, CancellationToken ct);
}

public interface ILoanReportService
{
    Task<LoanReportResult> RunAsync(Guid tenantId, Guid actorId, LoanReportFilter filter, CancellationToken ct);
}

public sealed class LoanReportFilter
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Status { get; set; }
    public string? Requester { get; set; }
    public string? Sector { get; set; }
    public string? DeliveryMode { get; set; }
    public bool? OverdueOnly { get; set; }
}

public sealed class LoanReportResult
{
    public IReadOnlyList<LoanReportRow> Rows { get; init; } = Array.Empty<LoanReportRow>();
    public int Total => Rows.Count;
    public int Overdue => Rows.Count(x => x.DaysLate > 0 && x.ReturnedAt is null);
    public double AverageDelayDays => Rows.Where(x => x.DaysLate > 0).Select(x => x.DaysLate).DefaultIfEmpty().Average();
    public int Collections => Rows.Sum(x => x.CollectionCount);
}

public sealed class LoanReportRow
{
    public Guid Id { get; set; }
    public long ProtocolNo { get; set; }
    public string RequesterName { get; set; } = "";
    public string? Sector { get; set; }
    public string Status { get; set; } = "";
    public string? DeliveryMode { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public int DaysLate { get; set; }
    public int CollectionCount { get; set; }
}
