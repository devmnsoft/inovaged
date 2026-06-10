using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class ProtocolAccessService : IProtocolAccessService
{
    private readonly IDbConnectionFactory _db;

    public ProtocolAccessService(IDbConnectionFactory db) => _db = db;

    public async Task<bool> CanViewProtocolAsync(Guid tenantId, Guid protocolId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (IsFullAdmin(user)) return true;
        var setores = await GetUserSectorIdsAsync(tenantId, userId, ct);
        if (setores.Length == 0) return false;
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.protocolo p
    where p.tenant_id=@TenantId
      and p.id=@ProtocolId
      and p.reg_status='A'
      and (
          p.setor_atual_id = any(@Setores)
          or p.setor_origem_id = any(@Setores)
          or exists(select 1 from ged.protocolo_setor_participante sp where sp.tenant_id=p.tenant_id and sp.protocolo_id=p.id and sp.setor_id=any(@Setores) and sp.pode_visualizar=true)
      )
);
""", new { TenantId = tenantId, ProtocolId = protocolId, Setores = setores }, cancellationToken: ct));
    }

    public async Task<bool> CanManageProtocolAsync(Guid tenantId, Guid protocolId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        if (IsFullAdmin(user)) return true;
        if (!HasRole(user, "ADMINISTRADOROPHIR")) return false;
        var setores = await GetUserSectorIdsAsync(tenantId, userId, ct);
        if (setores.Length == 0) return false;
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.protocolo
    where tenant_id=@TenantId
      and id=@ProtocolId
      and reg_status='A'
      and setor_atual_id = any(@Setores)
);
""", new { TenantId = tenantId, ProtocolId = protocolId, Setores = setores }, cancellationToken: ct));
    }

    private async Task<Guid[]> GetUserSectorIdsAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return Array.Empty<Guid>();
        await using var conn = await _db.OpenAsync(ct);
        var setores = await conn.QueryAsync<Guid>(new CommandDefinition("""
select setor_id
from ged.protocolo_usuario_setor
where tenant_id=@TenantId
  and usuario_id=@UserId
  and reg_status='A'
  and ativo=true
""", new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));
        return setores.ToArray();
    }

    private static bool IsFullAdmin(ClaimsPrincipal user) => HasRole(user, "ADMIN") || HasRole(user, "ADMINISTRADOR");
    private static bool HasRole(ClaimsPrincipal? user, string role)
    {
        var target = NormalizeRole(role);
        return user?.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Any(c => NormalizeRole(c.Value) == target) == true;
    }
    private static string NormalizeRole(string? value) => (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
}
