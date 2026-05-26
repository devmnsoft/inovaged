using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public interface ILoanRequestService
{
    Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, Guid? requesterId, bool canViewAll, CancellationToken ct);
    Task<LoanDetailsVM?> GetDetailsAsync(Guid tenantId, Guid loanId, Guid? requesterId, bool canViewAll, CancellationToken ct);
    Task<Result<Guid>> CreateAsync(Guid tenantId, Guid userId, LoanCreateVM vm, CancellationToken ct);
    Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> CancelAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> DeleteAsync(Guid tenantId, Guid loanId, Guid userId, string reason, CancellationToken ct);
    Task<Result> RegisterCollectionAsync(Guid tenantId, Guid loanId, Guid userId, string kind, string message, CancellationToken ct);
    Task<int> PendingCountAsync(Guid tenantId, Guid? userId, bool canViewAll, CancellationToken ct);
    Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string term, CancellationToken ct);
    Task<IReadOnlyList<LoanRowDto>> OverdueAsync(Guid tenantId, Guid? userId, bool canViewAll, CancellationToken ct);
}
