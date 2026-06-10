using System.Security.Claims;

namespace InovaGed.Web.Security;

public interface IMenuVisibilityService
{
    bool CanSeeGed(ClaimsPrincipal user);
    bool CanSeeHospitalDocuments(ClaimsPrincipal user);
    bool CanSeeLoansRequest(ClaimsPrincipal user);
    bool CanSeeLoansManage(ClaimsPrincipal user);
    bool CanSeeProtocolRequest(ClaimsPrincipal user);
    bool CanSeeProtocolManage(ClaimsPrincipal user);
    bool CanSeeSystemAdmin(ClaimsPrincipal user);
    bool CanSeeSchemaRepair(ClaimsPrincipal user);
    bool CanSeeLogs(ClaimsPrincipal user);
}

public sealed class MenuVisibilityService : IMenuVisibilityService
{
    public bool CanSeeGed(ClaimsPrincipal user) => AppMenuPolicy.CanSeeGed(user);
    public bool CanSeeHospitalDocuments(ClaimsPrincipal user) => AppMenuPolicy.CanSeeHospitalDocuments(user);
    public bool CanSeeLoansRequest(ClaimsPrincipal user) => AppMenuPolicy.CanSeeLoansRequest(user);
    public bool CanSeeLoansManage(ClaimsPrincipal user) => AppMenuPolicy.CanSeeLoansManage(user);
    public bool CanSeeProtocolRequest(ClaimsPrincipal user) => AppMenuPolicy.CanSeeProtocolRequest(user);
    public bool CanSeeProtocolManage(ClaimsPrincipal user) => AppMenuPolicy.CanSeeProtocolManage(user);
    public bool CanSeeSystemAdmin(ClaimsPrincipal user) => AppMenuPolicy.CanSeeSystemAdmin(user);
    public bool CanSeeSchemaRepair(ClaimsPrincipal user) => AppMenuPolicy.CanSeeSchemaRepair(user);
    public bool CanSeeLogs(ClaimsPrincipal user) => AppMenuPolicy.CanSeeLogs(user);
}
