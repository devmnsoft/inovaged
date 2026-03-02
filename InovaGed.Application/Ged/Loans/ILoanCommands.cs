using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public interface ILoanCommands
{
    Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, LoanCreateVM vm, CancellationToken ct);
    Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct);
    Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct);
    Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct);

    // Job/manual: marca evento OVERDUE e grava audit
    Task<Result<int>> RegisterOverdueEventsAsync(Guid tenantId, Guid? userId, CancellationToken ct);
}