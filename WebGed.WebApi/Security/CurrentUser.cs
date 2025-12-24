using System.Security.Claims;
using WebGed.Application.Common;

namespace WebGed.WebApi.Security;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http) => _http = http;

    public bool IsAuthenticated => _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid TenantId => Guid.Parse(_http.HttpContext!.User.FindFirstValue("tenant_id")!);
    public Guid UserId => Guid.Parse(_http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public string Email => _http.HttpContext!.User.FindFirstValue(ClaimTypes.Email) ?? "";
}
