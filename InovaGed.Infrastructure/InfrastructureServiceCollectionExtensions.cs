using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Codes;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Security;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Common.Time;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.DocumentQuality;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Batches;
using InovaGed.Application.Ged.Classification;
using InovaGed.Application.Ged.Dashboard;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Documents.Partials;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Ged.Physical;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Ged.Reports;
using InovaGed.Application.Ged.Search;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Operations;
using InovaGed.Application.Pacs;
using InovaGed.Application.Reports;
using InovaGed.Application.Parameters;
using InovaGed.Application.Retention;
using InovaGed.Application.RetentionCases;
using InovaGed.Application.RetentionTerms;
using InovaGed.Application.Search;
using InovaGed.Application.Security;
using InovaGed.Application.Security.Users;
using InovaGed.Application.Signatures;
using InovaGed.Application.SmartSearch;
using InovaGed.Application.SystemHealth;
using InovaGed.Application.Users;
using InovaGed.Application.Workflow;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Auth;
using InovaGed.Infrastructure.Classification;
using InovaGed.Infrastructure.ClassificationPlans;
using InovaGed.Infrastructure.Common.Codes;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Common.Security;
using InovaGed.Infrastructure.Common.Time;
using InovaGed.Infrastructure.DocumentGuardian;
using InovaGed.Infrastructure.DocumentQuality;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged.Batches;
using InovaGed.Infrastructure.Ged.Classification;
using InovaGed.Infrastructure.Ged.Dashboard;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Ged.Documents.Partials;
using InovaGed.Infrastructure.Ged.Folders;
using InovaGed.Infrastructure.Ged.Instruments;
using InovaGed.Infrastructure.Ged.Loans;
using InovaGed.Infrastructure.Ged.Physical;
using InovaGed.Infrastructure.Ged.Protocols;
using InovaGed.Infrastructure.Ged.Reports;
using InovaGed.Infrastructure.Ged.Search;
using InovaGed.Infrastructure.Instruments;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Operations;
using InovaGed.Infrastructure.Pacs;
using InovaGed.Infrastructure.Parameters;
using InovaGed.Infrastructure.Preview;
using InovaGed.Infrastructure.Reports;
using InovaGed.Infrastructure.Retention;
using InovaGed.Infrastructure.RetentionCases;
using InovaGed.Infrastructure.RetentionTerms;
using InovaGed.Infrastructure.Search;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Security.Users;
using InovaGed.Infrastructure.Signatures;
using InovaGed.Infrastructure.SmartSearch;
using InovaGed.Infrastructure.Storage;
using InovaGed.Infrastructure.SystemHealth;
using InovaGed.Infrastructure.Users;
using InovaGed.Infrastructure.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InovaGed.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInovaGedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddDatabaseModule(configuration)
            .AddGedModule(configuration)
            .AddOcrModule(configuration)
            .AddPreviewModule(configuration)
            .AddClassificationModule(configuration)
            .AddRetentionModule(configuration)
            .AddLoansModule(configuration)
            .AddGuardianModule(configuration)
            .AddWorkflowModule(configuration)
            .AddNotificationsModule(configuration)
            .AddSecurityOperationsModule(configuration);

        services.AddHealthChecks().AddCheck<InovaGedDependencyHealthCheck>("inovaged-dependencies");
        services.AddSingleton<IModuleCatalog, ModuleCatalog>();
        return services;
    }

    public static IServiceCollection AddDatabaseModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.")));
        return services;
    }

    public static IServiceCollection AddGedModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LocalStorageOptions>().Bind(configuration.GetSection("Storage:Local")).Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:Local:RootPath é obrigatório.").ValidateOnStart();
        services.Configure<DocumentUploadOptions>(configuration.GetSection("DocumentUpload"));
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
        services.AddScoped<IDocumentCommands, DocumentCommands>();
        services.AddScoped<IDocumentQueries, DocumentQueries>();
        services.AddScoped<IDocumentTypeCatalogQueries, DocumentTypeCatalogQueries>();
        services.AddScoped<IDocumentMoveService, DocumentMoveService>();
        services.AddScoped<IDocumentBulkUploadService, DocumentBulkUploadService>();
        services.AddScoped<IGedBulkDocumentActionService, GedBulkDocumentActionService>();
        services.AddScoped<IDocumentPartialService, DocumentPartialService>();
        services.AddScoped<IUploadFolderResolver, UploadFolderResolver>();
        services.AddScoped<IGedFolderMoveService, GedFolderMoveService>();
        services.AddSingleton<IUploadConcurrencyLimiter, UploadConcurrencyLimiter>();
        services.AddScoped<IGedProcessingJobRepository, GedProcessingJobRepository>();
        services.AddScoped<IUploadBatchService, UploadBatchService>();
        services.AddScoped<IUploadBatchConsistencyService, UploadBatchConsistencyService>();
        services.AddScoped<IUploadChunkService, UploadChunkService>();
        services.AddHostedService<StaleUploadBatchItemWorker>();
        services.AddHostedService<GedProcessingWorker>();
        services.AddScoped<IGedAccessPolicyService, GedAccessPolicyService>();
        services.AddScoped<IGedSearchService, GedSearchService>();
        services.AddScoped<IDocumentSearchQueries, DocumentSearchQueries>();
        services.AddScoped<IDocumentSearchTextQueries, DocumentSearchTextQueries>();
        services.AddScoped<IGedSmartSearchService, GedSmartSearchService>();
        services.AddScoped<ISmartSearchContextParser, SmartSearchContextParser>();
        services.AddScoped<ISmartQueryParser, SmartQueryParser>();
        services.AddScoped<IGedSmartQueryParser, SmartQueryParser>();
        services.AddScoped<IDocumentOcrMetadataExtractor, DocumentOcrMetadataExtractor>();
        services.AddScoped<IGedOcrMetadataExtractor, DocumentOcrMetadataExtractor>();
        services.AddScoped<ISmartSearchRepository, SmartSearchRepository>();
        services.AddScoped<IGedSmartSearchRepository, SmartSearchRepository>();
        services.AddScoped<ISmartSearchService, SmartSearchService>();
        services.AddScoped<IDocumentChatService, DocumentChatService>();
        services.AddScoped<ISearchStatisticsService, SearchStatisticsService>();
        services.AddScoped<IGedSearchSuggestionService, GedSearchSuggestionService>();
        services.AddScoped<IGedSearchStatisticsService, GedSearchStatisticsService>();
        services.AddScoped<IGedSearchIndexService, GedSearchIndexService>();
        services.AddScoped<IGedSmartSearchDiagnosticsService, GedSmartSearchDiagnosticsService>();
        services.AddScoped<IGedClassificationSuggestionService, GedClassificationSuggestionService>();
        services.AddScoped<IGedIntelligenceService, GedIntelligenceService>();
        services.AddScoped<IGedAdministrativeIntelligenceService, GedAdministrativeIntelligenceService>();
        services.AddScoped<IFolderQueries, FolderQueries>();
        services.AddScoped<IFolderNavigationResolver, FolderNavigationResolver>();
        services.AddScoped<IFolderCommands, FolderCommands>();
        services.AddScoped<IBatchQueries, BatchQueries>();
        services.AddScoped<IBatchCommands, BatchCommands>();
        services.AddScoped<IPhysicalQueries, PhysicalQueries>();
        services.AddScoped<IPhysicalCommands, PhysicalCommands>();
        services.AddScoped<IPopProcedureCommands, PopProcedureCommands>();
        services.AddScoped<IPopProcedureQueries, PopProcedureQueries>();
        services.AddScoped<InstrumentRepository>();
        return services;
    }

    public static IServiceCollection AddOcrModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OcrAutoScheduleOptions>(configuration.GetSection("OcrAutoSchedule"));
        services.Configure<OcrOptions>(configuration.GetSection("Ocr"));
        services.AddScoped<IOcrProcessRunner, OcrProcessRunner>();
        services.AddScoped<IOcrEnvironmentValidator, OcrEnvironmentValidator>();
        services.AddScoped<IOcrService, OcrMyPdfOcrService>();
        services.AddScoped<IPdfTextExtractor, PopplerPdfTextExtractor>();
        services.AddScoped<IOcrTextProvider, DbOcrTextProvider>();
        services.AddScoped<IOcrAutoClassificationService, OcrAutoClassificationService>();
        services.AddScoped<IOcrQueue, OcrQueue>();
        services.Configure<PacsIntegrationOptions>(configuration.GetSection("PacsIntegration"));
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<PacsIntegrationService>();
        services.AddScoped<IOcrJobRepository, OcrJobRepository>();
        services.AddScoped<IOcrAutoScheduleRepository, OcrAutoScheduleRepository>();
        services.AddScoped<IOcrAutoSchedulerService, OcrAutoSchedulerService>();
        services.AddScoped<OcrDashboardService>();
        services.AddScoped<IOcrDashboardService>(sp => sp.GetRequiredService<OcrDashboardService>());
        services.AddScoped<IOcrStatusResolver>(sp => sp.GetRequiredService<OcrDashboardService>());
        services.AddScoped<IOcrStatusQueries, OcrStatusQueries>();
        services.AddHostedService<OcrAutoSchedulerWorker>();
        if (configuration.GetValue<bool>("OcrWorker:Enabled")) services.AddHostedService<OcrWorker>();
        return services;
    }

    public static IServiceCollection AddPreviewModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LibreOfficeOptions>().Bind(configuration.GetSection("Preview")).ValidateOnStart();
        services.Configure<StorageLocalOptions>(configuration.GetSection("Storage:Local"));
        services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();
        services.AddScoped<IPreviewStatusRepository, PreviewStatusRepository>();
        services.AddSingleton<PreviewQueue>();
        services.AddSingleton<IPreviewJobQueue>(sp => sp.GetRequiredService<PreviewQueue>());
        services.AddHostedService<PreviewWorker>();
        return services;
    }

    public static IServiceCollection AddClassificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDocumentClassifier, RuleBasedDocumentClassifier>();
        services.AddScoped<IDocumentClassificationRepository, DocumentClassificationRepository>();
        services.AddScoped<IDocumentTypeQueries, DocumentTypeQueries>();
        services.AddScoped<IDocumentClassificationQueries, DocumentClassificationQueries>();
        services.AddScoped<IDocumentClassificationCommands, DocumentClassificationCommands>();
        services.AddScoped<IFolderClassificationRuleRepository, FolderClassificationRuleRepository>();
        services.AddScoped<IClassificationPendingCounter, ClassificationPendingCounter>();
        services.AddScoped<IClassificationDashboardQueries, ClassificationDashboardQueries>();
        services.AddScoped<IDocumentClassificationAuditQueries, DocumentClassificationAuditQueries>();
        services.AddScoped<InovaGed.Application.ClassificationPlans.IClassificationPlanRepository, ClassificationPlanRepository>();
        services.AddScoped<IClassificationPlanCommands, ClassificationPlanCommands>();
        services.AddScoped<IClassificationPlanQueries, ClassificationPlanQueries>();
        return services;
    }

    public static IServiceCollection AddRetentionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRetentionJobRepository, RetentionJobRepository>();
        services.AddScoped<RetentionRecalcService>();
        services.AddScoped<IRetentionRecalcService, RetentionRecalcService>();
        services.AddScoped<IRetentionQueueQueries, RetentionQueueQueries>();
        services.AddScoped<IRetentionAuditWriter, RetentionAuditWriter>();
        services.AddScoped<IRetentionQueueRepository, RetentionQueueRepository>();
        services.AddScoped<IRetentionQueueJob, RetentionQueueJob>();
        services.AddHostedService<RetentionDailyWorker>();
        services.AddScoped<IRetentionCaseRepository, RetentionCaseRepository>();
        services.AddScoped<IRetentionCaseExecutionRepository, RetentionCaseExecutionRepository>();
        services.AddScoped<RetentionCaseExecutionService>();
        services.AddScoped<IRetentionTermRepository, RetentionTermRepository>();
        services.AddScoped<ITermPdfGenerator, LibreOfficeTermPdfGenerator>();
        services.AddScoped<RetentionRecalculateService>();
        services.AddScoped<IDispositionReportsQueries, DispositionReportsQueries>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ISignatureProvider, InternalSignatureProvider>();
        return services;
    }

    public static IServiceCollection AddLoansModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LoanOverdueWorkerOptions>(configuration.GetSection("Workers:LoanOverdue"));
        services.AddScoped<ILoanQueries, LoanQueries>();
        services.AddScoped<ILoanCommands, LoanCommands>();
        services.AddScoped<ILoanHistoryWriter, LoanHistoryWriter>();
        services.AddScoped<ILoanRequestService, LoanRequestService>();
        services.AddScoped<IProtocolRequestService, ProtocolRequestService>();
        services.AddScoped<InovaGed.Application.Ged.Protocols.IProtocolAccessService, InovaGed.Infrastructure.Ged.Protocols.ProtocolAccessService>();
        services.AddScoped<IProtocolHistoryWriter, ProtocolHistoryWriter>();
        services.AddScoped<ILoanAccessService, LoanAccessService>();
        services.AddScoped<InovaGed.Application.Ged.Loans.IProtocolAccessService, InovaGed.Infrastructure.Ged.Loans.ProtocolAccessService>();
        services.AddScoped<ISolicitacaoService, SolicitacaoService>();
        services.AddScoped<ISecureDocumentLinkService, SecureDocumentLinkService>();
        if (configuration.GetValue<bool>("Workers:LoanOverdue:Enabled")) services.AddHostedService<LoanOverdueWorker>();
        return services;
    }

    public static IServiceCollection AddGuardianModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentQualityOptions>(configuration.GetSection("DocumentQuality"));
        services.AddScoped<IDocumentGuardianService, DocumentGuardianService>();
        services.AddScoped<IDocumentQualityAnalyzerService, DocumentQualityAnalyzerService>();
        services.AddHostedService<DocumentQualitySchedulerWorker>();
        return services;
    }

    public static IServiceCollection AddWorkflowModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDocumentWorkflowQueries, DocumentWorkflowQueries>();
        services.AddScoped<IWorkflowQueries, WorkflowQueries>();
        services.AddScoped<IDocumentWorkflowCommands, DocumentWorkflowCommands>();
        services.AddScoped<IWorkflowCommands, WorkflowCommands>();
        return services;
    }

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration) => services;

    public static IServiceCollection AddSecurityOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITenantTimeZoneService, TenantTimeZoneService>();
        services.Configure<SchemaRepairOptions>(configuration.GetSection("SchemaRepair"));
        services.AddScoped<ISchemaFixSqlProvider, SchemaFixSqlProvider>();
        services.AddScoped<ISchemaHealthService, SchemaHealthService>();
        services.AddScoped<IHomologationHealthService, HomologationHealthService>();
        services.AddScoped<ISchemaRepairService, SchemaRepairService>();
        services.AddSingleton<ISchemaCompatibilityState, SchemaCompatibilityState>();
        services.AddScoped<ITableSchemaGuard, TableSchemaGuard>();
        services.AddScoped<IOperationsDashboardService, OperationsDashboardService>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<ICodeGeneratorService, CodeGeneratorService>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IPermissionChecker, AllowAllPermissionChecker>();
        services.AddScoped<PermissionService>();
        services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<PermissionService>());
        services.AddScoped<IParameterRepository, ParameterRepository>();
        services.AddScoped<IAppAuditLogService, AppAuditLogService>();
        services.AddScoped<ISystemLogQueryService, SystemLogQueryService>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        services.AddScoped<IAuditSecurityService, AuditSecurityService>();
        services.AddScoped<IAbacAuthorizationService, AbacAuthorizationService>();
        services.AddScoped<ISensitiveDocumentCryptoService, SensitiveDocumentCryptoService>();
        services.AddScoped<IUserAdminRepository, UserAdminRepository>();
        services.AddScoped<IUserAdminQueries, UserAdminQueries>();
        services.AddScoped<UserService>();
        return services;
    }
}
