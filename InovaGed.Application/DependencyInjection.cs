using InovaGed.Application.Classification;
using InovaGed.Application.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InovaGed.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddInovaGedApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<DocumentAppService>();
        services.AddScoped<DocumentClassificationAppService>();
        services.AddScoped<SimpleTextDocumentTypeSuggester>();
        services.AddScoped<HybridDocumentTypeSuggester>();

        return services;
    }
}
