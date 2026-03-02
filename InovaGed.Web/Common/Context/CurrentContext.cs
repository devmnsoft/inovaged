using System.Security.Claims;
using InovaGed.Application.Common.Context;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Web.Common.Context;

public sealed class CurrentContext : ICurrentContext
{
    private readonly IHttpContextAccessor _http;

    public CurrentContext(IHttpContextAccessor http)
    {
        _http = http;
    }

    public bool IsAuthenticated =>
        _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid TenantId
    {
        get
        {
            // Claim padrão recomendado: "tenant_id"
            var s = _http.HttpContext?.User?.FindFirstValue("tenant_id");
            if (Guid.TryParse(s, out var id))
                return id;

            // fallback opcional (remova em produção se quiser forçar login)
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        }
    }

    public Guid UserId
    {
        get
        {
            var s =
                _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _http.HttpContext?.User?.FindFirstValue("sub");

            if (Guid.TryParse(s, out var id))
                return id;

            return Guid.Empty;
        }
    }

    public string? UserEmail =>
        _http.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    public string? UserDisplay
    {
        get
        {
            var user = _http.HttpContext?.User;
            if (user == null) return null;

            // prioridade:
            // 1. Claim custom "name"
            // 2. Claim Name
            // 3. Email
            // 4. Fallback Identity.Name

            return user.FindFirstValue("name")
                ?? user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue(ClaimTypes.Email)
                ?? user.Identity?.Name;
        }
    }
}