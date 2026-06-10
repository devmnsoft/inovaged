using System.Security.Claims;

namespace InovaGed.Application.Ged.Loans;

public interface ILoanAccessService
{
    Task<bool> CanViewLoanAsync(Guid tenantId, Guid loanId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
    Task<bool> CanManageLoanAsync(Guid tenantId, Guid loanId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
    Task<LoanVisibilityScope> BuildLoanScopeAsync(Guid tenantId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
}
