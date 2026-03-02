using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public interface ILoanQueries
{
    Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, CancellationToken ct);
    Task<LoanDetailsVM?> GetAsync(Guid tenantId, Guid loanId, CancellationToken ct);
    Task<IReadOnlyList<LoanRowDto>> ListOverdueAsync(Guid tenantId, CancellationToken ct);
}