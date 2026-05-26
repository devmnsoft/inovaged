using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security;

namespace InovaGed.Infrastructure.Security;

public sealed class GedAccessPolicyService : IGedAccessPolicyService
{
    private readonly IDbConnectionFactory _dbFactory;

    public GedAccessPolicyService(IDbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public bool IsAdmin(ClaimsPrincipal principal) => HasRole(principal, "ADMIN");
    public bool IsAdministradorOphir(ClaimsPrincipal principal) => HasRole(principal, "ADMINISTRADOROPHIR");
    public bool IsArquivistaOphir(ClaimsPrincipal principal) => HasRole(principal, "ARQUIVISTAOPHIR");

    public async Task<bool> IsAdminAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct)
        => (principal is not null && IsAdmin(principal)) || await HasRoleInDatabaseAsync(tenantId, userId, "ADMIN", ct);

    public async Task<bool> IsAdministradorOphirAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct)
        => (principal is not null && IsAdministradorOphir(principal)) || await HasRoleInDatabaseAsync(tenantId, userId, "ADMINISTRADOROPHIR", ct);

    public async Task<bool> IsArquivistaOphirAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct)
        => (principal is not null && IsArquivistaOphir(principal)) || await HasRoleInDatabaseAsync(tenantId, userId, "ARQUIVISTAOPHIR", ct);

    public async Task<bool> CanAccessGedAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => await IsAdminAsync(tenantId, userId, principal, ct) || (!await IsAdministradorOphirAsync(tenantId, userId, principal, ct) && !await IsArquivistaOphirAsync(tenantId, userId, principal, ct));

    public Task<bool> CanAccessHospitalDocumentsAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => Task.FromResult(true);

    public async Task<bool> CanAccessLoansAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => await IsAdminAsync(tenantId, userId, principal, ct) || await IsAdministradorOphirAsync(tenantId, userId, principal, ct) || await IsArquivistaOphirAsync(tenantId, userId, principal, ct);

    public Task<bool> CanAccessGlobalDashboardAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    public Task<bool> CanManageOcrAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    public Task<bool> CanMoveDocumentAsync(Guid tenantId, Guid userId, Guid documentId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    private static bool HasRole(ClaimsPrincipal principal, string role)
    {
        var target = NormalizeRole(role);
        return principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Any(c => NormalizeRole(c.Value) == target);
    }

    private async Task<bool> HasRoleInDatabaseAsync(Guid tenantId, Guid userId, string role, CancellationToken ct)
    {
        const string sql = @"
select distinct role_name
from (
    select ar.name as role_name
      from ged.app_user_role aur
      join ged.app_role ar on ar.id = aur.role_id and ar.tenant_id = @tenantId
     where aur.user_id = @userId
    union all
    select r.name as role_name
      from ged.user_roles ur
      join ged.role r on r.id = ur.role_id
     where ur.user_id = @userId and ur.tenant_id = @tenantId
) q";

        await using var conn = await _dbFactory.OpenAsync(ct);
        var roles = await conn.QueryAsync<string>(new CommandDefinition(sql, new { tenantId, userId }, cancellationToken: ct));
        var target = NormalizeRole(role);
        return roles.Any(r => NormalizeRole(r) == target);
    }

    private static string NormalizeRole(string? value)
        => (value ?? string.Empty).Trim().Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
}

