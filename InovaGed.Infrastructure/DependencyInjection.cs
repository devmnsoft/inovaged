using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Search;
using InovaGed.Application.Security;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Classification;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.DocumentGuardian;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Search;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InovaGed.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInovaGedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LocalStorageOptions>()
            .Bind(configuration.GetSection("Storage:Local"))
            .ValidateOnStart();
        services.AddOptions<StorageLocalOptions>()
            .Bind(configuration.GetSection("Storage:Local"))
            .ValidateOnStart();
        services.AddOptions<LibreOfficeOptions>()
            .Bind(configuration.GetSection("Preview"))
            .ValidateOnStart();

        services.TryAddSingleton<IDbConnectionFactory>(_ =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");
            return new NpgsqlConnectionFactory(connectionString);
        });

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();
        services.AddScoped<IPdfTextExtractor, PopplerPdfTextExtractor>();
        services.AddScoped<IDocumentMoveService, DocumentMoveService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IDocumentGuardianService, DocumentGuardianService>();

        services.AddScoped<IDocumentClassifier, RuleBasedDocumentClassifier>();
        services.AddScoped<IDocumentClassificationRepository, DocumentClassificationRepository>();
        services.AddScoped<IDocumentTypeQueries, DocumentTypeQueries>();
        services.AddScoped<IDocumentClassificationQueries, DocumentClassificationQueries>();
        services.AddScoped<IDocumentCommands, DocumentCommands>();
        services.AddScoped<IDocumentSearchQueries, DocumentSearchQueries>();
        services.AddScoped<IDocumentSearchTextQueries, DocumentSearchTextQueries>();

        return services;
    }
}
