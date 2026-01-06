namespace InovaGed.Application.Identity;

public static class PermissionCheckerExtensions
{
    public static async Task DemandAsync(
        this IPermissionChecker perm,
        Guid tenantId,
        Guid userId,
        string permissionCode,
        CancellationToken ct = default)
    {
        if (perm is null) throw new ArgumentNullException(nameof(perm));

        var ok = await perm.IsAllowedAsync(tenantId, userId, permissionCode, ct);
        if (!ok)
            throw new UnauthorizedAccessException($"Permissão negada: {permissionCode}");
    }
}
