using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace InovaGed.Application.Common.Security;

public interface IAccessFailureLogger
{
    Task LogAsync(HttpContext ctx, AuthorizationPolicy? policy, int statusCode, string reason);
}