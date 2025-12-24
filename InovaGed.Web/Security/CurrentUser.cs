using System.Security.Claims;
using InovaGed.Application.Identity;

namespace InovaGed.Web.Security;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http) => _http = http;

    public bool IsAuthenticated =>
        _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid TenantId => GetGuid("tenant_id");
    public Guid UserId => GetGuid(ClaimTypes.NameIdentifier);

    public string Email =>
        _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        _http.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList()
        ?? new List<string>();

    private Guid GetGuid(string claimType)
    {
        var raw = _http.HttpContext?.User?.FindFirstValue(claimType);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
