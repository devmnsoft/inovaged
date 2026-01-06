namespace InovaGed.Application.Identity;

/// <summary>
/// Checador de permissões usado pela camada Application (sem depender de ASP.NET).
/// A implementação real fica na Infrastructure.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> IsAllowedAsync(
        Guid tenantId,
        Guid userId,
        string permissionCode,
        CancellationToken ct = default);
}
