using InovaGed.Application.Classification;
using InovaGed.Application.Documents;
using InovaGed.Application.Auth;
using InovaGed.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InovaGed.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddInovaGedApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddScoped<ICurrentUserAccessor, CurrentUserAccessorAdapter>();

        services.AddScoped<DocumentAppService>();
        services.AddScoped<DocumentClassificationAppService>();
        services.AddScoped<SimpleTextDocumentTypeSuggester>();
        services.AddScoped<HybridDocumentTypeSuggester>();
        services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();

        return services;
    }
}

public sealed class AnonymousCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public Guid TenantId => Guid.Empty;
    public Guid UserId => Guid.Empty;
    public string Email => string.Empty;
    public IReadOnlyList<string> Roles => Array.Empty<string>();
}

