using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ocr;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Security;
using InovaGed.Application.SystemHealth;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.DocumentGuardian;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.SystemHealth;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using InovaGed.Application.Identity;

namespace InovaGed.Infrastructure;

/// <summary>
/// Composição compartilhada dos serviços centrais do InovaGED.
/// Deve ser usada pelos hosts MVC e WebApi para evitar registros divergentes.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInovaGedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddDatabaseModule(configuration)
            .AddGedModule(configuration)
            .AddOcrModule(configuration)
            .AddPreviewModule(configuration)
            .AddGuardianModule(configuration)
            .AddSecurityOperationsModule(configuration)
            .AddInfrastructureHealthModule(configuration);

        return services;
    }

    public static IServiceCollection AddDatabaseModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IDbConnectionFactory>(_ =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");
            return new NpgsqlConnectionFactory(connectionString);
        });

        services.AddInfrastructureModule("Database", true, [], !string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")), HealthStatus.Healthy);
        return services;
    }

    public static IServiceCollection AddGedModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddOptions<LocalStorageOptions>()
            .Bind(configuration.GetSection("Storage:Local"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "Storage:Local:RootPath é obrigatório.")
            .ValidateOnStart();
        services.AddOptions<StorageLocalOptions>()
            .Bind(configuration.GetSection("Storage:Local"))
            .ValidateOnStart();

        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
        services.AddScoped<IDocumentMoveService, DocumentMoveService>();

        services.AddInfrastructureModule("GED", true, ["Database", "Storage"], !string.IsNullOrWhiteSpace(configuration["Storage:Local:RootPath"]), HealthStatus.Healthy);
        services.AddInfrastructureModule("Storage", true, ["FileSystem"], !string.IsNullOrWhiteSpace(configuration["Storage:Local:RootPath"]), HealthStatus.Healthy);
        return services;
    }

    public static IServiceCollection AddOcrModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OcrOptions>()
            .Bind(configuration.GetSection("Ocr"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IPdfTextExtractor, PopplerPdfTextExtractor>();
        services.AddScoped<IOcrService, OcrMyPdfOcrService>();

        services.AddInfrastructureModule("OCR", configuration.GetValue("Ocr:Enabled", true), ["Database", "Storage"], true, HealthStatus.Degraded);
        return services;
    }

    public static IServiceCollection AddPreviewModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LibreOfficeOptions>()
            .Bind(configuration.GetSection("Preview"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();
        services.AddInfrastructureModule("Preview", true, ["Storage", "LibreOffice"], true, HealthStatus.Degraded);
        return services;
    }

    public static IServiceCollection AddGuardianModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IDocumentGuardianService, DocumentGuardianService>();
        services.AddInfrastructureModule("Guardian", true, ["Database", "GED"], true, HealthStatus.Healthy);
        return services;
    }

    public static IServiceCollection AddSecurityOperationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IPermissionChecker, AllowAllPermissionChecker>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddInfrastructureModule("SecurityOperations", true, ["Database"], true, HealthStatus.Healthy);
        return services;
    }

    public static IServiceCollection AddInfrastructureHealthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<ISecretMasker, SecretMasker>();

        services.TryAddSingleton<
            IStartupConfigurationValidator,
            StartupConfigurationValidator>();

        services.TryAddSingleton<
            IExecutableResolver,
            ExecutableResolver>();

        services.TryAddSingleton<
            IInfrastructureModuleCatalog,
            InfrastructureModuleCatalog>();

        services
            .AddHealthChecks()
            .AddCheck<InovaGedDependencyHealthCheck>(
                "inovaged-dependencies");

        return services;
    }

    private static IServiceCollection AddInfrastructureModule(
        this IServiceCollection services,
        string name,
        bool enabled,
        IReadOnlyList<string> dependencies,
        bool configurationValid,
        HealthStatus health,
        string? lastFailure = null)
    {
        var version = typeof(InfrastructureServiceCollectionExtensions).Assembly.GetName().Version?.ToString() ?? "n/a";
        services.AddSingleton(new InfrastructureModuleDescriptor(name, enabled, dependencies, configurationValid, health, version, lastFailure));
        return services;
    }

}
