using System.Security.Claims;

namespace InovaGed.Application.Security;

public interface IGedAccessPolicyService
{
    Task<bool> IsAdminAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct);
    Task<bool> IsAdministradorOphirAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct);
    Task<bool> IsArquivistaOphirAsync(Guid tenantId, Guid userId, ClaimsPrincipal? principal, CancellationToken ct);

    bool IsAdmin(ClaimsPrincipal principal);
    bool IsAdministradorOphir(ClaimsPrincipal principal);
    bool IsArquivistaOphir(ClaimsPrincipal principal);

    Task<bool> CanAccessGedAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanAccessHospitalDocumentsAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanAccessLoansAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanAccessGlobalDashboardAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanAccessManualAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanManageOcrAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanManageUsersAsync(Guid tenantId, Guid userId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanMoveDocumentAsync(Guid tenantId, Guid userId, Guid documentId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanMoveFolderAsync(Guid tenantId, Guid userId, Guid folderId, ClaimsPrincipal principal, CancellationToken ct);
    Task<bool> CanUploadDocumentToFolderAsync(Guid tenantId, Guid userId, Guid? folderId, ClaimsPrincipal principal, CancellationToken ct);
}
