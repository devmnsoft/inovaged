using System.Data;
using System.Security.Claims;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Security;

public sealed class GedAccessPolicyService : IGedAccessPolicyService
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly ILogger<GedAccessPolicyService> _logger;

    public GedAccessPolicyService(IDbConnectionFactory dbFactory, ILogger<GedAccessPolicyService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public bool IsAdmin(ClaimsPrincipal principal) => HasRole(principal, "ADMIN") || HasRole(principal, "ADMINISTRADOR");
    public bool IsAdministradorOphir(ClaimsPrincipal principal) => HasRole(principal, "ADMINISTRADOROPHIR");
    public bool IsArquivistaOphir(ClaimsPrincipal principal) => HasRole(principal, "ARQUIVISTAOPHIR");

    public async Task<bool> CanAccessHospitalDocumentsAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => await IsAdminAsync(tenantId, userId, principal, ct)
           || await IsAdministradorOphirAsync(tenantId, userId, principal, ct)
           || await IsArquivistaOphirAsync(tenantId, userId, principal, ct)
           || HasRole(principal, "HOSPITAL")
           || HasRole(principal, "ARQUIVISTA")
           || HasRole(principal, "GESTOR")
           || HasRole(principal, "OPERADOR");

    public async Task<bool> IsAdminAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct)
        => (principal is not null && IsAdmin(principal)) || await HasRoleInDatabaseAsync(tenantId, userId, "ADMIN", ct) || await HasRoleInDatabaseAsync(tenantId, userId, "ADMINISTRADOR", ct);

    public async Task<bool> IsAdministradorOphirAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct)
        => (principal is not null && IsAdministradorOphir(principal)) || await HasRoleInDatabaseAsync(tenantId, userId, "ADMINISTRADOROPHIR", ct);

    public async Task<bool> IsArquivistaOphirAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct)
        => (principal is not null && IsArquivistaOphir(principal)) || await HasRoleInDatabaseAsync(tenantId, userId, "ARQUIVISTAOPHIR", ct);

    public async Task<bool> CanAccessGedAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => await IsAdminAsync(tenantId, userId, principal, ct) || (!await IsAdministradorOphirAsync(tenantId, userId, principal, ct) && !await IsArquivistaOphirAsync(tenantId, userId, principal, ct));

    public async Task<bool> CanAccessLoansAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => await IsAdminAsync(tenantId, userId, principal, ct)
           || await IsAdministradorOphirAsync(tenantId, userId, principal, ct)
           || await IsArquivistaOphirAsync(tenantId, userId, principal, ct);

    public Task<bool> CanAccessGlobalDashboardAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    public async Task<bool> CanAccessManualAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => await IsAdminAsync(tenantId, userId, principal, ct)
           || (!await IsAdministradorOphirAsync(tenantId, userId, principal, ct) && !await IsArquivistaOphirAsync(tenantId, userId, principal, ct));

    public Task<bool> CanManageOcrAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    public Task<bool> CanManageUsersAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    public Task<bool> CanMoveDocumentAsync(Guid tenantId, Guid userId, Guid documentId, ClaimsPrincipal principal, CancellationToken ct)
        => IsAdminAsync(tenantId, userId, principal, ct);

    public async Task<bool> CanUploadDocumentToFolderAsync(Guid tenantId, Guid userId, Guid? folderId, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (await IsAdminAsync(tenantId, userId, principal, ct)) return true;
        if (await IsAdministradorOphirAsync(tenantId, userId, principal, ct)) return false;
        if (await IsArquivistaOphirAsync(tenantId, userId, principal, ct)) return false;
        return await CanAccessGedAsync(tenantId, userId, principal, ct);
    }

    private static bool HasRole(ClaimsPrincipal principal, string role)
    {
        var target = NormalizeRole(role);
        return principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Any(c => NormalizeRole(c.Value) == target);
    }

    private async Task<bool> HasRoleInDatabaseAsync(Guid tenantId, Guid userId, string role, CancellationToken ct)
    {
        try
        {
            await using var conn = await _dbFactory.OpenAsync(ct);
            var roleNormalized = NormalizeRole(role);
            var found = await QueryRoleExistsAsync(conn, tenantId, userId, roleNormalized, ct);
            return found;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Consulta de role cancelada. Tenant={TenantId} User={UserId}", tenantId, userId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar roles no banco. Tenant={TenantId} User={UserId}", tenantId, userId);
            return false;
        }
    }

    private static async Task<bool> QueryRoleExistsAsync(IDbConnection conn, Guid tenantId, Guid userId, string normalizedRole, CancellationToken ct)
    {
        const string sql = @"
select exists (
    select 1
    from (
        select upper(replace(replace(replace(coalesce(ar.normalized_name, ar.name), ' ', ''), '_', ''), '-', '')) as role_name
          from ged.app_user_role aur
          join ged.app_role ar on ar.id = aur.role_id
         where aur.user_id = @UserId
           and ar.tenant_id = @TenantId
        union all
        select upper(replace(replace(replace(coalesce(r.code, r.name), ' ', ''), '_', ''), '-', '')) as role_name
          from ged.user_roles ur
          join ged.role r on r.id = ur.role_id
         where ur.user_id = @UserId
           and ur.tenant_id = @TenantId
           and r.tenant_id = @TenantId
    ) x
    where x.role_name = @Role
);";

        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql,
            new { UserId = userId, TenantId = tenantId, Role = normalizedRole }, cancellationToken: ct));
    }

    private static string NormalizeRole(string? value)
    {
        return (value ?? "")
            .Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .ToUpperInvariant();
    }
}
