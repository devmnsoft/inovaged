using System.Security.Claims;
using InovaGed.Application.Identity;

namespace WebGed.WebApi.Security;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http) => _http = http;

    public bool IsAuthenticated => _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid TenantId => GetRequiredGuidClaim("tenant_id");

    public Guid UserId => GetRequiredGuidClaim(ClaimTypes.NameIdentifier);

    public string Email => GetOptionalClaim(ClaimTypes.Email) ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        _http.HttpContext?.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
        ?? Array.Empty<string>();

    private Guid GetRequiredGuidClaim(string claimType)
    {
        var value = GetOptionalClaim(claimType);

        if (!Guid.TryParse(value, out var id))
        {
            throw new UnauthorizedAccessException($"Claim obrigatória inválida: {claimType}.");
        }

        return id;
    }

    private string? GetOptionalClaim(string claimType)
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("Usuário não autenticado.");
        }

        return user.FindFirstValue(claimType);
    }
}
