using InovaGed.Application.Ged.Loans;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanOverdueService(ILoanCommands commands) : ILoanOverdueService
{
    public async Task<int> RunAsync(Guid tenantId, Guid? actorId, CancellationToken ct)
    {
        var result = await commands.RegisterOverdueEventsAsync(tenantId, actorId, ct);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.ErrorMessage);
    }
}
