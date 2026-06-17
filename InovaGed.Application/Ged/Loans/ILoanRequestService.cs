using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public interface ILoanRequestService
{
    Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, Guid? requesterId, LoanVisibilityScope scope, CancellationToken ct);
    Task<LoanDetailsVM?> GetDetailsAsync(Guid tenantId, Guid loanId, Guid? requesterId, LoanVisibilityScope scope, CancellationToken ct);
    Task<Result<Guid>> CreateAsync(Guid tenantId, Guid userId, LoanCreateVM vm, CancellationToken ct);
    Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> CancelAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> RejectAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> ReturnForAdjustmentAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct);
    Task<Result> DeleteAsync(Guid tenantId, Guid loanId, Guid userId, string reason, CancellationToken ct);
    Task<Result> RegisterCollectionAsync(Guid tenantId, Guid loanId, Guid userId, string kind, string message, CancellationToken ct);
    Task<int> PendingCountAsync(Guid tenantId, Guid? userId, LoanVisibilityScope scope, CancellationToken ct);
    Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string term, CancellationToken ct);
    Task<IReadOnlyList<LoanRowDto>> OverdueAsync(Guid tenantId, Guid? userId, LoanVisibilityScope scope, CancellationToken ct);
}

public sealed class LoanVisibilityScope
{
    public bool IsFullAdmin { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsAdministradorOphir { get; set; }
    public bool IsArquivistaOphir { get; set; }
    public Guid TenantId { get; set; }
    public string? SectorId { get; set; }
    public string? Sector { get; set; }
    public Guid? UserId { get; set; }
    public bool CanSeeAll { get; set; }
    public bool OnlyOwnRequests { get; set; }
    public bool CanManage { get; set; }

    public void BuildLoanScope()
    {
        CanSeeAll = IsAdmin;
        CanManage = IsAdmin || IsAdministradorOphir;
        OnlyOwnRequests = !IsAdmin && !IsAdministradorOphir;
    }
}
