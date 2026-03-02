namespace InovaGed.Application.Security
{
    public interface IPermissionService
    {
        Task<bool> HasAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct);
    }
}
