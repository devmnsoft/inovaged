namespace InovaGed.Application.Administration;

public enum PermissionMode { LEGACY, AUDIT_ONLY, ENFORCED }
public enum AdministrationHealthState { Saudavel, Atencao, Indisponivel, Desabilitado, NaoConfigurado, Desconhecido }

public sealed record AdministrationMetric(string Code, string Title, string? Value, AdministrationHealthState State, string? Reason = null, string? Guidance = null, string? Icon = null);
public sealed record AdministrationActionRecommendation(string Title, string Reason, string Guidance, string Severity);
public sealed record AdministrationOverview(IReadOnlyList<AdministrationMetric> Metrics, IReadOnlyList<AdministrationActionRecommendation> Recommendations);
public sealed record TenantSecurityConfiguration(Guid TenantId, PermissionMode PermissionMode, DateTimeOffset ChangedAt, string? ChangedBy, string? ChangeReason, string RegStatus);
public sealed record PermissionCatalogItem(string Code, string Description, string Module, string Roles, int UsersAffected, string Status, string Origin, DateTimeOffset? LastChangedAt);
public sealed record IdentityMigrationSummary(int TotalUsers, int FromServidor, int AlreadyMigrated, int Divergent, int WithoutCpf, int InvalidCpf, int MultipleCpfs, int LegacyDependent);
public sealed record AdministrationListItem(string Name, string Status, string Detail, string? Tenant = null, DateTimeOffset? LastActivity = null);
public sealed record ComplianceControlItem(string Code, string Title, string State, string Evidence, string Guidance);

public interface IAdministrationDashboardService
{
    Task<AdministrationOverview> GetOverviewAsync(Guid? tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantSecurityConfiguration>> GetSecurityConfigurationsAsync(Guid? tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionCatalogItem>> GetPermissionCatalogAsync(string? search, CancellationToken ct = default);
    Task<IdentityMigrationSummary> GetIdentityMigrationSummaryAsync(Guid? tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetUsersAsync(Guid? tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetAuditEventsAsync(Guid? tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetTenantsAsync(Guid? tenantId, bool globalView, CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetWorkersAsync(Guid? tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetHealthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetSafeConfigurationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdministrationListItem>> GetMigrationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ComplianceControlItem>> GetComplianceAsync(Guid? tenantId, CancellationToken ct = default);
}

public static class CpfProtection
{
    public static string Normalize(string value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    public static bool IsValid(string value)
    {
        var cpf = Normalize(value);
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;
        int Digit(int len) { var sum = 0; for (var i = 0; i < len; i++) sum += (cpf[i] - '0') * (len + 1 - i); var r = sum % 11; return r < 2 ? 0 : 11 - r; }
        return Digit(9) == cpf[9] - '0' && Digit(10) == cpf[10] - '0';
    }
    public static string Mask(string value)
    {
        var cpf = Normalize(value);
        return cpf.Length == 11 ? $"***.***.***-{cpf[^2..]}" : "***.***.***-**";
    }
    public static string SearchHash(string normalizedCpf, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("A chave de pesquisa de CPF deve vir de configuração segura.");
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Normalize(normalizedCpf)))).ToLowerInvariant();
    }
}
