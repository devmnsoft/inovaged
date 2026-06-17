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

    public async Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, Guid? requesterId, LoanVisibilityScope scope, CancellationToken ct)
    {
        var sector = NormalizeSector(scope.Sector) ?? await ResolveUserSectorAsync(tenantId, requesterId, ct);
        scope.Sector = sector;
        if (scope.IsAdministradorOphir && string.IsNullOrWhiteSpace(sector)) return Array.Empty<LoanRowDto>();
        return await _queries.ListScopedAsync(tenantId, q, status, requesterId, scope, ct);
    }

    public async Task<LoanDetailsVM?> GetDetailsAsync(Guid tenantId, Guid loanId, Guid? requesterId, LoanVisibilityScope scope, CancellationToken ct)
    {
        if (!await CanAccessAsync(tenantId, loanId, requesterId, scope, ct)) return null;
        return await _queries.GetAsync(tenantId, loanId, ct);
    }

    public Task<Result<Guid>> CreateAsync(Guid tenantId, Guid userId, LoanCreateVM vm, CancellationToken ct)
        => _commands.CreateAsync(tenantId, userId, vm, ct);

    public Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.ApproveAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> CancelAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.CancelAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.DeliverAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.ReturnAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> RejectAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.RejectAsync(tenantId, loanId, userId, notes, ct);
    public Task<Result> ReturnForAdjustmentAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct) => _commands.ReturnForAdjustmentAsync(tenantId, loanId, userId, notes, ct);

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

    public async Task<int> PendingCountAsync(Guid tenantId, Guid? userId, LoanVisibilityScope scope, CancellationToken ct)
    {
        var rows = await ListAsync(tenantId, null, "REQUESTED", userId, scope, ct);
        return rows.Count;
    }

    public Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string term, CancellationToken ct) => _queries.SearchDocumentsAsync(tenantId, term, ct);

    public async Task<IReadOnlyList<LoanRowDto>> OverdueAsync(Guid tenantId, Guid? userId, LoanVisibilityScope scope, CancellationToken ct)
    {
        var rows = await _queries.ListOverdueAsync(tenantId, ct);
        if (scope.IsAdmin) return rows;
        var allowed = await ListAsync(tenantId, null, null, userId, scope, ct);
        var ids = allowed.Select(x => x.Id).ToHashSet();
        return rows.Where(x => ids.Contains(x.Id)).ToList();
    }

    private async Task<bool> CanAccessAsync(Guid tenantId, Guid loanId, Guid? requesterId, LoanVisibilityScope scope, CancellationToken ct)
    {
        if (scope.IsFullAdmin || scope.IsAdmin) return true;
        var sector = NormalizeSector(scope.Sector) ?? await ResolveUserSectorAsync(tenantId, requesterId, ct);
        await using var con = await _db.OpenAsync(ct);
        const string sql = """
select exists (
  select 1 from ged.loan_request
  where tenant_id=@TenantId and id=@LoanId and reg_status='A'
    and (
      (@IsAdministradorOphir = true and @Sector is not null and (nullif(coalesce(requester_sector_name, requester_sector, ''),'') = @Sector or nullif(coalesce(assigned_sector_name,''),'') = @Sector or nullif(coalesce(current_sector_name,''),'') = @Sector))
      or (coalesce(@IsAdministradorOphir,false) = false and (requester_id = @RequesterId or created_by = @RequesterId))
    )
);
""";
        return await con.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { TenantId = tenantId, LoanId = loanId, RequesterId = requesterId, IsAdministradorOphir = scope.IsAdministradorOphir, Sector = sector }, cancellationToken: ct));
    }

    private async Task<string?> ResolveUserSectorAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        if (userId is null || userId == Guid.Empty) return null;
        await using var con = await _db.OpenAsync(ct);
        const string sql = """
select nullif(coalesce(s.setor, s.lotacao, ''), '')
from ged.app_user u
left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id
where u.tenant_id=@TenantId and u.id=@UserId
limit 1;
""";
        return NormalizeSector(await con.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { TenantId = tenantId, UserId = userId }, cancellationToken: ct)));
    }

    private static string? NormalizeSector(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
