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

    public Task<bool> CanMoveFolderAsync(Guid tenantId, Guid userId, Guid folderId, ClaimsPrincipal principal, CancellationToken ct)
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
        var roles = await UserRoleReader.ReadAsync((System.Data.Common.DbConnection)conn, null, tenantId, userId, ct);
        return roles.Contains(NormalizeRole(normalizedRole));
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
