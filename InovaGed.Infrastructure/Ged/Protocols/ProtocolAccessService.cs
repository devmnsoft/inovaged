using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Protocols;

namespace InovaGed.Infrastructure.Ged.Protocols;

public sealed class ProtocolAccessService : IProtocolAccessService
{
    private readonly IDbConnectionFactory _db;
    public ProtocolAccessService(IDbConnectionFactory db) => _db = db;

    public async Task<ProtocolVisibilityScope> BuildScopeAsync(Guid tenantId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (IsFullAdmin(user)) return new ProtocolVisibilityScope { IsAdmin = true };
        var isAdministradorOphir = HasRole(user, "ADMINISTRADOROPHIR");
        var isArquivistaOphir = HasRole(user, "ARQUIVISTAOPHIR");
        var sector = await ResolveSectorAsync(tenantId, userId, ct);
        return new ProtocolVisibilityScope
        {
            IsAdministradorOphir = isAdministradorOphir,
            IsArquivistaOphir = isArquivistaOphir,
            SectorId = sector.Id,
            SectorName = sector.Name
        };
    }

    public async Task<bool> CanViewAsync(Guid tenantId, Guid protocolRequestId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope = await BuildScopeAsync(tenantId, userId, user, ct);
        if (scope.IsAdmin) return true;
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
  select 1 from ged.protocol_request p
  where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A'
    and (
      (@IsManage = true and ((@SectorId is not null and p.assigned_sector_id=@SectorId) or p.assigned_user_id=@UserId))
      or p.requester_user_id=@UserId
    )
);
""", new { TenantId = tenantId, Id = protocolRequestId, UserId = userId, IsManage = scope.CanManage, scope.SectorId }, cancellationToken: ct));
    }

    public async Task<bool> CanManageAsync(Guid tenantId, Guid protocolRequestId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope = await BuildScopeAsync(tenantId, userId, user, ct);
        if (scope.IsAdmin) return true;
        if (!scope.IsAdministradorOphir) return false;
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
  select 1 from ged.protocol_request p
  where p.tenant_id=@TenantId and p.id=@Id and p.reg_status='A'
    and ((@SectorId is not null and p.assigned_sector_id=@SectorId) or p.assigned_user_id=@UserId)
);
""", new { TenantId = tenantId, Id = protocolRequestId, UserId = userId, scope.SectorId }, cancellationToken: ct));
    }

    private async Task<(Guid? Id, string? Name)> ResolveSectorAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue || userId == Guid.Empty) return (null, null);
        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid? Id, string? Name)>(new CommandDefinition("""
select s.id as Id, nullif(coalesce(s.setor, s.lotacao, ''), '') as Name
from ged.app_user u
left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id
where u.tenant_id=@TenantId and u.id=@UserId
limit 1;
""", new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));
        return row;
    }

    private static bool IsFullAdmin(ClaimsPrincipal user) => HasRole(user, "ADMIN") || HasRole(user, "ADMINISTRADOR");
    private static bool HasRole(ClaimsPrincipal? user, string role)
    {
        var target = Normalize(role);
        return user?.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Any(c => Normalize(c.Value) == target) == true;
    }
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
}
