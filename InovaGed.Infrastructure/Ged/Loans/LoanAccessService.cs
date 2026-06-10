using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanAccessService : ILoanAccessService
{
    private readonly IDbConnectionFactory _db;

    public LoanAccessService(IDbConnectionFactory db) => _db = db;

    public async Task<bool> CanViewLoanAsync(Guid tenantId, Guid loanId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope = await BuildLoanScopeAsync(tenantId, userId, user, ct);
        if (scope.CanSeeAll) return true;
        await using var conn = await _db.OpenAsync(ct);
        var sector = NormalizeSector(scope.Sector);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.loan_request
    where tenant_id=@TenantId
      and id=@LoanId
      and reg_status='A'
      and (
          (@IsAdministradorOphir = true and @Sector is not null and nullif(coalesce(requester_sector,''),'') = @Sector)
          or (@IsArquivistaOphir = true and requester_id = @UserId)
      )
);
""", new { TenantId = tenantId, LoanId = loanId, UserId = userId, scope.IsAdministradorOphir, scope.IsArquivistaOphir, Sector = sector }, cancellationToken: ct));
    }

    public async Task<bool> CanManageLoanAsync(Guid tenantId, Guid loanId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope = await BuildLoanScopeAsync(tenantId, userId, user, ct);
        if (!scope.CanManage) return false;
        if (scope.CanSeeAll) return true;
        await using var conn = await _db.OpenAsync(ct);
        var sector = NormalizeSector(scope.Sector);
        if (string.IsNullOrWhiteSpace(sector)) return false;
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from ged.loan_request
    where tenant_id=@TenantId
      and id=@LoanId
      and reg_status='A'
      and nullif(coalesce(requester_sector,''),'') = @Sector
);
""", new { TenantId = tenantId, LoanId = loanId, Sector = sector }, cancellationToken: ct));
    }

    public async Task<LoanVisibilityScope> BuildLoanScopeAsync(Guid tenantId, Guid? userId, ClaimsPrincipal user, CancellationToken ct)
    {
        var scope = new LoanVisibilityScope
        {
            IsAdmin = HasRole(user, "ADMIN") || HasRole(user, "ADMINISTRADOR"),
            IsAdministradorOphir = HasRole(user, "ADMINISTRADOROPHIR"),
            IsArquivistaOphir = HasRole(user, "ARQUIVISTAOPHIR"),
            UserId = userId,
            TenantId = tenantId
        };

        if (userId.HasValue)
        {
            await using var conn = await _db.OpenAsync(ct);
            scope.Sector = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select nullif(coalesce(s.setor, s.lotacao, ''), '')
from ged.app_user u
left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id
where u.tenant_id=@TenantId and u.id=@UserId
limit 1
""", new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));
        }

        scope.SectorId = scope.Sector;
        scope.BuildLoanScope();
        return scope;
    }

    private static bool HasRole(ClaimsPrincipal? user, string role)
    {
        var target = NormalizeRole(role);
        return user?.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Any(c => NormalizeRole(c.Value) == target) == true;
    }

    private static string NormalizeRole(string? value) => (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
    private static string? NormalizeSector(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
