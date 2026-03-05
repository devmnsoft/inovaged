using InovaGed.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Web.Security;

public sealed class HttpTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _http;

    public HttpTenantProvider(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string? TenantId
    {
        get
        {
            // ajuste conforme sua regra (header, claim, host, etc.)
            // Exemplo: header X-Tenant
            var ctx = _http.HttpContext;
            if (ctx is null) return null;

            if (ctx.Request.Headers.TryGetValue("X-Tenant", out var v))
                return v.ToString();

            // fallback: tenant default da PoC
            return "00000000-0000-0000-0000-000000000001";
        }
    }
}