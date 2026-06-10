using InovaGed.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace InovaGed.Web.Security;

public sealed class AccessFailureAuditHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccessFailureAuditHandler> _logger;

    public AccessFailureAuditHandler(IServiceScopeFactory scopeFactory, ILogger<AccessFailureAuditHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Succeeded)
        {
            if (authorizeResult.Forbidden && context.User.IsFullAdmin())
            {
                _logger.LogWarning(
                    "FullAdmin recebeu acesso negado indevido. Liberando request por regra de segurança administrativa. Path={Path} User={User}",
                    context.Request.Path,
                    context.User.Identity?.Name ?? "(sem nome)");
                await next(context);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<IAccessFailureLogger>();

                var reason = authorizeResult.Forbidden ? "ACCESS_DENIED" : "AUTHENTICATION_REQUIRED";
                var statusCode = authorizeResult.Forbidden
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized;

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
