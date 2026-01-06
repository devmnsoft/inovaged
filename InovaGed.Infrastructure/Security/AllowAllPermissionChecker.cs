using System;
using System.Threading;
using System.Threading.Tasks;
using InovaGed.Application.Identity;

namespace InovaGed.Infrastructure.Security;

/// <summary>
/// Implementação provisória: libera tudo.
/// Trocar depois por checagem real (roles/permissões em tabela).
/// </summary>
public sealed class AllowAllPermissionChecker : IPermissionChecker
{
    public Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default)
        => Task.FromResult(true);
}
