
using Dapper;
using InovaGed.Application.Administration;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Administration;

public sealed class PermissionGovernanceRepository : IPermissionGovernanceRepository
{
    private readonly IDbConnectionFactory _db;
    public PermissionGovernanceRepository(IDbConnectionFactory db) => _db = db;
    public async Task<PermissionMode> GetModeAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.tenant_security_configuration') is not null", cancellationToken: ct))) return PermissionMode.LEGACY;
        var mode = await c.ExecuteScalarAsync<string?>(new CommandDefinition("select permission_mode from ged.tenant_security_configuration where tenant_id=@tenantId and reg_status='A' order by changed_at desc limit 1", new { tenantId }, cancellationToken: ct));
        return Enum.TryParse<PermissionMode>(mode, true, out var parsed) ? parsed : PermissionMode.LEGACY;
    }
    public async Task LogEvaluationAsync(PermissionEvaluationContext context, bool legacyAllowed, bool realAllowed, PermissionMode mode, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select to_regclass('ged.permission_evaluation_log') is not null", cancellationToken: ct))) return;
        await c.ExecuteAsync(new CommandDefinition(@"insert into ged.permission_evaluation_log(tenant_id,user_id,permission_code,module,route,action,legacy_result,real_result,permission_mode,correlation_id,evaluated_at,reg_status)
values(@TenantId,@UserId,@PermissionCode,@Module,@Route,@Action,@legacyAllowed,@realAllowed,@mode,@CorrelationId,now() at time zone 'utc','A')", new { context.TenantId, context.UserId, context.PermissionCode, context.Module, context.Route, context.Action, legacyAllowed, realAllowed, mode = mode.ToString(), context.CorrelationId }, cancellationToken: ct));
    }
}
