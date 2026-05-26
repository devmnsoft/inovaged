using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanRequestService : ILoanRequestService
{
    private readonly ILoanQueries _queries;
    private readonly ILoanCommands _commands;
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<LoanRequestService> _logger;

    public LoanRequestService(ILoanQueries queries, ILoanCommands commands, IDbConnectionFactory db, ILogger<LoanRequestService> logger)
    {
        _queries = queries;
        _commands = commands;
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, Guid? requesterId, bool canViewAll, CancellationToken ct)
    {
        var rows = await _queries.ListAsync(tenantId, q, status, ct);
        return canViewAll || requesterId is null ? rows : rows.Where(x => x.RequesterName is not null).ToList();
    }

    public Task<LoanDetailsVM?> GetDetailsAsync(Guid tenantId, Guid loanId, Guid? requesterId, bool canViewAll, CancellationToken ct)
        => _queries.GetAsync(tenantId, loanId, ct);

    public Task<Result<Guid>> CreateAsync(Guid tenantId, Guid userId, LoanCreateVM vm, CancellationToken ct)
        => _commands.CreateAsync(tenantId, userId, vm, ct);

    public Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.ApproveAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> CancelAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.CancelAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.DeliverAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.ReturnAsync(tenantId, loanId, userId, notes, ct);

    public async Task<Result> DeleteAsync(Guid tenantId, Guid loanId, Guid userId, string reason, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            var rows = await con.ExecuteAsync(new CommandDefinition(@"update ged.loan_request
set reg_status='I', deleted_at=now(), deleted_by=@UserId, delete_reason=@Reason
where tenant_id=@TenantId and id=@LoanId and reg_status='A'", new { TenantId = tenantId, LoanId = loanId, UserId = userId, Reason = reason }, cancellationToken: ct));
            return rows > 0 ? Result.Ok() : Result.Fail("NOT_FOUND", "Solicitação não encontrada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteAsync failed");
            return Result.Fail("ERROR", "Erro ao excluir solicitação.");
        }
    }

    public async Task<Result> RegisterCollectionAsync(Guid tenantId, Guid loanId, Guid userId, string kind, string message, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            await con.ExecuteAsync(new CommandDefinition("insert into ged.loan_collection_event(tenant_id,loan_id,event_at,kind,message,created_by) values(@TenantId,@LoanId,now(),@Kind,@Message,@UserId)", new { TenantId = tenantId, LoanId = loanId, Kind = kind, Message = message, UserId = userId }, cancellationToken: ct));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RegisterCollectionAsync failed");
            return Result.Fail("ERROR", "Erro ao registrar cobrança.");
        }
    }

    public async Task<int> PendingCountAsync(Guid tenantId, Guid? userId, bool canViewAll, CancellationToken ct)
    {
        var stats = await _queries.StatsAsync(tenantId, ct);
        return stats.Requested;
    }

    public Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string term, CancellationToken ct) => _queries.SearchDocumentsAsync(tenantId, term, ct);
    public Task<IReadOnlyList<LoanRowDto>> OverdueAsync(Guid tenantId, Guid? userId, bool canViewAll, CancellationToken ct) => _queries.ListOverdueAsync(tenantId, ct);
}
