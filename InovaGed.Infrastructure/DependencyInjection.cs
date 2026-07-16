using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Security;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.DocumentGuardian;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InovaGed.Application.Identity;

namespace InovaGed.Infrastructure;

/// <summary>
/// Composição compartilhada dos serviços centrais do InovaGED.
/// Deve ser usada pelos hosts MVC e WebApi para evitar registros divergentes.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInovaGedApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<DocumentAppService>();
        return services;
    }

    public static IServiceCollection AddInovaGedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMemoryCache();

        services.AddOptions<LocalStorageOptions>()
            .Bind(configuration.GetSection("Storage:Local"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Storage:Local:RootPath é obrigatório.")
            .ValidateOnStart();

        services.AddOptions<LibreOfficeOptions>()
            .Bind(configuration.GetSection("Preview"))
            .ValidateOnStart();

        services.AddSingleton<IDbConnectionFactory>(_ =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");
            return new NpgsqlConnectionFactory(connectionString);
        });

        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();
        services.AddScoped<IPdfTextExtractor, PopplerPdfTextExtractor>();
        services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IPermissionChecker, AllowAllPermissionChecker>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IDocumentMoveService, DocumentMoveService>();
        services.AddScoped<IDocumentGuardianService, DocumentGuardianService>();

        services.AddHealthChecks()
            .AddCheck<InovaGedDependencyHealthCheck>("inovaged-dependencies");

        return services;
    }
}
