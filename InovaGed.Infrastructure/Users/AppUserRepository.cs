using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security.Users;

namespace InovaGed.Infrastructure.Security.Users;

public sealed class AppUserRepository : IAppUserRepository
{
    private readonly IDbConnectionFactory _db;
    public AppUserRepository(IDbConnectionFactory db) => _db = db;

    public async Task<AppUserByCpfDto?> GetByCpfAsync(Guid tenantId, string cpf, CancellationToken ct)
    {
        // PoC: CPF está em password_plain (como no seu select)
        const string sql = @"
select
  id        as Id,
  tenant_id as TenantId,
  name      as Name,
  email     as Email,
  is_active as IsActive,
  password_plain as Cpf
from ged.app_user
where tenant_id = @tenantId
  and password_plain = @cpf
limit 1;";

        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<AppUserByCpfDto>(sql, new { tenantId, cpf });
    }
}