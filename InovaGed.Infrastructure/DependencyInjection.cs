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
using InovaGed.Application.Administration;
using InovaGed.Infrastructure.Administration;
using InovaGed.Application.Continuity;
using InovaGed.Application.Readiness;
using InovaGed.Infrastructure.Readiness;
using InovaGed.Infrastructure.Continuity;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using InovaGed.Application.Identity;
using InovaGed.Application.Signatures;
using InovaGed.Infrastructure.Signatures;
using InovaGed.Application.Cluster;
using InovaGed.Infrastructure.Cluster;
using InovaGed.Infrastructure.Tenants;
using InovaGed.Application.SystemHealth.Migrations;
using InovaGed.Infrastructure.SystemHealth.Migrations;
using InovaGed.Application.SmartGed;
using InovaGed.Infrastructure.SmartGed;
using InovaGed.Application.SmartGed.Assistant;
using InovaGed.Infrastructure.SmartGed.Assistant;
using InovaGed.Application.SmartWorkflow;
using InovaGed.Infrastructure.SmartWorkflow;
using InovaGed.Application.Governance;
using InovaGed.Infrastructure.Governance;
using InovaGed.Application.ContractMeasurement;
using InovaGed.Infrastructure.ContractMeasurement;

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
            .AddInfrastructureHealthModule(configuration)
            .AddContinuityModule(configuration)
            .AddDigitalSignatureModule(configuration);

        services.AddOptions<NodeIdentityOptions>()
            .Bind(configuration.GetSection("Cluster"))
            .Validate(o => o.HeartbeatSeconds >= 5 && o.NodeExpirationSeconds > o.HeartbeatSeconds, "Cluster heartbeat/expiration inválidos.")
            .Validate(o => o.Mode == ClusterMode.SingleNode || !o.RequireDistributedDependencies ||
                (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")) &&
                 string.Equals(configuration["Storage:Provider"], "SharedFileSystem", StringComparison.OrdinalIgnoreCase) &&
                 new[] { "SharedFileSystem", "Redis" }.Contains(configuration["DataProtection:Provider"], StringComparer.OrdinalIgnoreCase)),
                "MultiNode/BlueGreen requer Redis, storage compartilhado e Data Protection compartilhado.")
            .ValidateOnStart();
        services.AddSingleton<INodeIdentity, NodeIdentity>();
        services.AddSingleton<IClusterNodeRegistry, PostgresClusterNodeRegistry>();
        services.AddScoped<IClusterLeaseManager, PostgresClusterLeaseManager>();
        if (configuration.GetValue<ClusterMode>("Cluster:Mode") != ClusterMode.SingleNode)
            services.AddHostedService<ClusterHeartbeatWorker>();

        return services;
    }

    /// <summary>
    /// Registers the database-backed services used by the critical administration,
    /// labels, retention and physical archive routes. Keeping this composition here
    /// makes missing constructor dependencies visible to every web host.
    /// </summary>
    public static IServiceCollection AddStabilityCriticalServices(this IServiceCollection services)
    {
        services.AddScoped<GovernanceService>();
        services.AddScoped<ContractMeasurementService>();
        services.AddScoped<IContractMeasurementDashboardService>(sp => sp.GetRequiredService<ContractMeasurementService>());
        services.AddScoped<IContractServiceCatalogService>(sp => sp.GetRequiredService<ContractMeasurementService>());
        services.AddScoped<IContractProductivityService>(sp => sp.GetRequiredService<ContractMeasurementService>());
        services.AddScoped<IContractMeasurementPeriodService>(sp => sp.GetRequiredService<ContractMeasurementService>());
        services.AddScoped<global::InovaGed.Application.ContractMeasurement.IContractGlosaService>(sp => sp.GetRequiredService<ContractMeasurementService>());
        services.AddScoped<IContractMeasurementReportService>(sp => sp.GetRequiredService<ContractMeasurementService>());
        services.AddScoped<IGovernanceDashboardService>(sp => sp.GetRequiredService<GovernanceService>());
        services.AddScoped<IGovernanceAuditService>(sp => sp.GetRequiredService<GovernanceService>());
        services.AddScoped<IGovernanceAlertService>(sp => sp.GetRequiredService<GovernanceService>());
        services.AddScoped<IGovernanceEvidenceService>(sp => sp.GetRequiredService<GovernanceService>());
        services.AddScoped<IGovernanceReportService>(sp => sp.GetRequiredService<GovernanceService>());
        services.AddScoped<IDocumentMetadataExtractor, LocalDocumentMetadataExtractor>();
        services.AddScoped<SmartGedService>();
        services.AddScoped<IDocumentIntelligenceService>(sp => sp.GetRequiredService<SmartGedService>());
        services.AddScoped<IDocumentClassificationSuggestionService>(sp => sp.GetRequiredService<SmartGedService>());
        services.AddScoped<IDocumentRetentionSuggestionService>(sp => sp.GetRequiredService<SmartGedService>());
        services.AddScoped<ISmartGedSearchService>(sp => sp.GetRequiredService<SmartGedService>());
        services.AddScoped<ISmartAssistantRetrievalService, LocalSmartAssistantRetrievalService>();
        services.AddScoped<ISmartAssistantAnswerComposer, LocalSmartAssistantAnswerComposer>();
        services.AddScoped<ISmartGedAssistantService, SmartGedAssistantService>();
        services.AddScoped<SmartWorkflowService>();
        services.AddScoped<ISmartWorkflowTaskService>(sp => sp.GetRequiredService<SmartWorkflowService>());
        services.AddScoped<ISmartWorkflowNumberGenerator>(sp => sp.GetRequiredService<SmartWorkflowService>());
        services.AddScoped<ISmartWorkflowRuleEngine, SmartWorkflowRuleEngine>();
        services.AddScoped<Release.Uat.UatService>();
        services.AddScoped<global::InovaGed.Application.Release.Uat.IUatTestPlanService>(sp => sp.GetRequiredService<Release.Uat.UatService>());
        services.AddScoped<global::InovaGed.Application.Release.Uat.IUatExecutionService>(sp => sp.GetRequiredService<Release.Uat.UatService>());
        services.AddScoped<global::InovaGed.Application.Release.Uat.IReleaseEvidenceService>(sp => sp.GetRequiredService<Release.Uat.UatService>());
        services.AddScoped<global::InovaGed.Application.SystemHealth.Incidents.IExceptionClassifier, SystemHealth.Incidents.ExceptionClassifier>();
        services.AddScoped<SystemHealth.Incidents.SystemIncidentService>();
        services.AddScoped<global::InovaGed.Application.SystemHealth.Incidents.ISystemIncidentService>(sp => sp.GetRequiredService<SystemHealth.Incidents.SystemIncidentService>());
        services.AddScoped<global::InovaGed.Application.SystemHealth.Incidents.IRouteHealthRecorder>(sp => sp.GetRequiredService<SystemHealth.Incidents.SystemIncidentService>());
        services.AddScoped<global::InovaGed.Application.Retention.IPcdVersionResolver, Retention.PcdVersionResolver>();
        services.AddScoped<global::InovaGed.Application.Retention.IRetentionAuditWriter, Retention.RetentionAuditWriter>();
        services.AddScoped<global::InovaGed.Application.Retention.IRetentionDestinationRepository, Retention.RetentionDestinationRepository>();

        services.AddScoped<global::InovaGed.Application.Labels.Printing.ILabelPrintJobService, PhysicalArchive.LabelPrintJobService>();
        services.AddScoped<global::InovaGed.Application.Labels.Printing.ILabelPdfRenderService, PhysicalArchive.LabelHtmlPdfRenderService>();
        services.AddScoped<global::InovaGed.Application.Labels.Tracking.ILabelTrackingService, PhysicalArchive.LabelTrackingService>();
        services.AddScoped<global::InovaGed.Application.Labels.Tracking.ILabelInventoryService, PhysicalArchive.LabelInventoryService>();
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.ILabelTemplateCatalogService, PhysicalArchive.LabelTemplateCatalogService>();

        services.AddScoped<PhysicalArchive.ArchiveReconciliationService>();
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.Reconciliation.IArchiveReconciliationService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveReconciliationService>());
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.Reconciliation.IArchiveReconciliationEngine>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveReconciliationService>());
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.Reconciliation.IArchiveReconciliationFixService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveReconciliationService>());

        services.AddScoped<PhysicalArchive.ArchiveProductivityService>();
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.Productivity.IArchiveServiceCatalogService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveProductivityService>());
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.Productivity.IArchiveProductivityService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveProductivityService>());
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.Productivity.IArchiveBillingService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveProductivityService>());

        services.AddScoped<PhysicalArchive.ArchiveWorkOrderService>();
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.WorkOrders.IArchiveWorkOrderService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveWorkOrderService>());
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.WorkOrders.IArchiveWorkOrderTaskService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveWorkOrderService>());
        services.AddScoped<global::InovaGed.Application.PhysicalArchive.WorkOrders.IArchiveWorkOrderSlaService>(sp => sp.GetRequiredService<PhysicalArchive.ArchiveWorkOrderService>());

        services.AddScoped<Contracts.Fiscalization.ContractFiscalizationService>();
        services.AddScoped<global::InovaGed.Application.Contracts.Fiscalization.IContractFiscalizationService>(sp => sp.GetRequiredService<Contracts.Fiscalization.ContractFiscalizationService>());
        services.AddScoped<global::InovaGed.Application.Contracts.Fiscalization.IContractGlosaService>(sp => sp.GetRequiredService<Contracts.Fiscalization.ContractFiscalizationService>());
        services.AddScoped<global::InovaGed.Application.Contracts.Fiscalization.IContractFiscalizationReportService>(sp => sp.GetRequiredService<Contracts.Fiscalization.ContractFiscalizationService>());
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
        services.TryAddScoped<ITenantCatalog, DatabaseTenantCatalog>();
        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

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
        services.AddScoped<IAdministrationDashboardService, AdministrationDashboardService>();
        services.AddScoped<IRealPermissionChecker, DatabasePermissionChecker>();
        services.AddScoped<IPermissionGovernanceRepository, PermissionGovernanceRepository>();
        services.AddScoped<IPermissionChecker, CompositePermissionChecker>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddInfrastructureModule("SecurityOperations", true, ["Database"], true, HealthStatus.Healthy);
        return services;
    }


    public static IServiceCollection AddContinuityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OperationsOptions>().Bind(configuration.GetSection("Operations")).ValidateOnStart();
        services.AddOptions<BackupOptions>().Bind(configuration.GetSection("Backup")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PortabilityOptions>().Bind(configuration.GetSection("Portability")).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<IModuleReadinessService, PostgresModuleReadinessService>();
        services.AddScoped<IAdministrativeTenantScopeResolver, AdministrativeTenantScopeResolver>();
        services.AddScoped<IBackupPolicyService, BackupPolicyRepository>();
        services.AddScoped<IBackupCatalogService, BackupCatalogRepository>();
        services.AddScoped<IRecoveryObjectiveService, RecoveryObjectiveService>();
        services.AddScoped<IPortabilityExportService, PortabilityExportRepository>();
        services.AddScoped<IRecoveryPlanService, RecoveryPlanRepository>();
        services.AddScoped<ITenantOffboardingService, TenantOffboardingRepository>();
        services.AddScoped<IDataDeletionWorkflowService, DataDeletionWorkflowService>();
        services.AddScoped<IBackupOrchestrator, BackupOrchestrator>();
        services.AddScoped<IBackupIntegrityService, BackupIntegrityService>();
        services.AddScoped<IRestoreValidationService, RestoreValidationService>();
        services.AddScoped<IPortabilityManifestService, PortabilityManifestService>();
        services.AddScoped<IPortabilityPackageVerifier, PortabilityPackageVerifier>();
        services.AddScoped<IPostgresBackupProvider, PostgresBackupProvider>();
        services.AddScoped<IBackupRetentionService, BackupRetentionService>();
        services.AddInfrastructureModule("ContinuityPortability", configuration.GetValue("Backup:Enabled", false) || configuration.GetValue("Portability:Enabled", false), ["Database", "Storage"], true, HealthStatus.Degraded);
        return services;
    }


    public static IServiceCollection AddDigitalSignatureModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("DigitalSignature");
        var enabled = section.GetValue("Enabled", false);
        var mode = section.GetValue("Mode", "AgentCms");
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        services.AddOptions<DigitalSignatureOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(options => !options.AllowedAgentOrigins.Any(o => o == "*"), "DigitalSignature:AllowedAgentOrigins não pode conter origem curinga.")
            .Validate(options => !options.RequireLoopbackHttps || options.AgentBaseUrl.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase) || options.AgentBaseUrl.StartsWith("https://[::1]", StringComparison.OrdinalIgnoreCase), "AgentBaseUrl deve ser HTTPS loopback quando RequireLoopbackHttps=true.")
            .Validate(options => Uri.TryCreate(options.PublicServerBaseUrl, UriKind.Absolute, out var publicUri) && publicUri.Scheme == Uri.UriSchemeHttps, "DigitalSignature:PublicServerBaseUrl deve ser uma URL HTTPS absoluta.")
            .Validate(options => !(options.Enabled && options.RequireCertificateIdentityMatch) || (!string.IsNullOrWhiteSpace(options.CertificateIdentityHmacKeyVersion) && !string.IsNullOrWhiteSpace(options.CertificateIdentityHmacKey) && options.CertificateIdentityHmacKey.Length >= 32), "DigitalSignature:CertificateIdentityHmacKey e versão são obrigatórios com pelo menos 32 caracteres quando a conferência de identidade está habilitada.")
            .Validate(options => !string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase) || !options.AllowServerSidePfxUpload, "Upload PFX server-side é bloqueado em Production.")
            .Validate(options => !string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase) || !options.AllowInternalTestCertificates, "Certificados internos de teste são bloqueados em Production.")
            .ValidateOnStart();

        services.TryAddScoped<ICertificateIdentityService, CertificateIdentityService>();

        if (enabled && string.Equals(mode, "AgentCms", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ISigningSessionRepository, PostgresSigningSessionRepository>();
            services.AddScoped<ISignatureRepository, PostgresSignatureRepository>();
            services.AddScoped<ISignatureEvidenceRepository, PostgresSignatureEvidenceRepository>();
            services.AddScoped<ISignatureValidationRepository, PostgresSignatureValidationRepository>();
            services.AddScoped<ISignatureEventRepository, PostgresSignatureEventRepository>();
            services.AddScoped<IDocumentVersionSigningContentService, DocumentVersionSigningContentService>();
            services.AddScoped<ISignaturePackageService, SignaturePackageService>();
            services.AddScoped<ISignatureValidationService, CmsDetachedSignatureValidationService>();
            services.AddScoped<ISignatureValidationOutcomeFactory, SignatureValidationOutcomeFactory>();
            services.AddScoped<ISigningUnitOfWorkFactory, PostgresSigningUnitOfWorkFactory>();
            services.AddScoped<ISigningOrchestrator, CmsSigningOrchestrator>();
        }
        else
        {
            services.AddScoped<ISigningSessionRepository, NoopSigningSessionRepository>();
            services.AddScoped<ISignatureEvidenceRepository, NoopSignatureEvidenceRepository>();
            services.AddScoped<ISignatureValidationService, NotConfiguredSignatureValidationService>();
            services.AddScoped<ISigningOrchestrator, NotConfiguredSigningOrchestrator>();
        }

        services.AddInfrastructureModule("DigitalSignature", enabled, ["Database", "Storage"], true, enabled ? HealthStatus.Degraded : HealthStatus.Healthy);
        return services;
    }

    public static IServiceCollection AddInfrastructureHealthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<ISecretMasker, SecretMasker>();
        services.TryAddSingleton<IDatabaseConfigurationValidator, PostgresConfigurationValidator>();

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
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<InovaGedDependencyHealthCheck>(
                "inovaged-dependencies", tags: ["ready"]);

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

    public static IServiceCollection AddInovaGedWebRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddInovaGedApplication(configuration);
        services.AddInovaGedInfrastructure(configuration);
        return services;
    }

    public static IServiceCollection AddInovaGedApiRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddInovaGedApplication(configuration);
        services.AddInovaGedInfrastructure(configuration);
        return services;
    }

    public static IServiceCollection AddInovaGedWorkerRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddInovaGedApplication(configuration);
        services.AddInovaGedInfrastructure(configuration);
        return services;
    }

}
