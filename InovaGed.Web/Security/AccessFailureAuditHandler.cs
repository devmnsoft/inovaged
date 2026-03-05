using InovaGed.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace InovaGed.Web.Security;

public sealed class AccessFailureAuditHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public AccessFailureAuditHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Succeeded)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<IAccessFailureLogger>();

                var reason = authorizeResult.Forbidden ? "FORBIDDEN" : "CHALLENGE";
                var statusCode = authorizeResult.Forbidden
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized;

                // ✅ ASSINATURA REAL DA SUA INTERFACE
                await logger.LogAsync(context, policy, statusCode, reason);
            }
            catch
            {
                // não derruba request por auditoria
            }
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}