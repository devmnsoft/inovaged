using System.Security.Claims;

namespace InovaGed.Application.Ged.Loans;

public interface IProtocolAccessService
{
    Task<bool> CanViewProtocolAsync(Guid tenantId, Guid protocolId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
    Task<bool> CanManageProtocolAsync(Guid tenantId, Guid protocolId, Guid? userId, ClaimsPrincipal user, CancellationToken ct);
}
