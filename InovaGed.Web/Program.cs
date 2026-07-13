using InovaGed.Application.HospitalAnalytics;
using InovaGed.Application.HospitalIntelligence;
using InovaGed.Application.HospitalTrends;
using InovaGed.Infrastructure.HospitalAnalytics;
using InovaGed.Infrastructure.HospitalIntelligence;
using InovaGed.Application.Ged.Intelligence;
using InovaGed.Infrastructure.Ged.Intelligence;
using InovaGed.Infrastructure.HospitalTrends;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Codes;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Notifications;
using InovaGed.Application.Common.Security;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.DocumentQuality;
using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Batches;
using InovaGed.Application.Ged.Classification;
using InovaGed.Application.Ged.Dashboard;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Ged.Physical;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Application.Ged.Reports;
using InovaGed.Application.Ged.Search;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Pacs;
using InovaGed.Application.Parameters;
using InovaGed.Application.Reports;
using InovaGed.Application.Retention;
using InovaGed.Application.RetentionCases;
using InovaGed.Application.RetentionTerms;
using InovaGed.Application.Search;
using InovaGed.Application.SmartSearch;
using InovaGed.Application.Security.Users;
using InovaGed.Application.Signatures;
using InovaGed.Application.Users;
using InovaGed.Application.Workflow;
using InovaGed.Infrastructure;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Auth;
using InovaGed.Infrastructure.Classification;
using InovaGed.Infrastructure.ClassificationPlans;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Common.Codes;
using InovaGed.Infrastructure.Common.Security;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.DocumentQuality;
using InovaGed.Infrastructure.Ged;
using InovaGed.Infrastructure.Ged.Batches;
using InovaGed.Infrastructure.Ged.Classification;
using InovaGed.Infrastructure.Ged.Dashboard;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Application.Ged.Documents.Partials;
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
using InovaGed.Application.Operations;
using InovaGed.Infrastructure.Operations;
using InovaGed.Infrastructure.Pacs;
using InovaGed.Infrastructure.Parameters;
using InovaGed.Infrastructure.Preview;
using InovaGed.Web.Notifications;
using InovaGed.Infrastructure.Reports;
using InovaGed.Infrastructure.Retention;
using InovaGed.Infrastructure.RetentionCases;
using InovaGed.Infrastructure.RetentionTerms;
using InovaGed.Infrastructure.Search;
using InovaGed.Infrastructure.SmartSearch;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Security.Users;
using InovaGed.Infrastructure.Signatures;
using InovaGed.Infrastructure.Storage;
using InovaGed.Infrastructure.Users;
using InovaGed.Infrastructure.Workflow;
using InovaGed.Web.Auth;
using InovaGed.Web.Common.Context;
using InovaGed.Web.Middleware;
using InovaGed.Web.Hubs;
using InovaGed.Web.ocr;
using InovaGed.Web.Security;
using InovaGed.Web.Services;
using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using InovaGed.Application.Preview;
using InovaGed.Application.Security;
using InovaGed.Application.SystemHealth;
using InovaGed.Infrastructure.SystemHealth;
using InovaGed.Infrastructure.Common.Time;
using InovaGed.Infrastructure.Tenants;
using InovaGed.Infrastructure.Jobs;
using InovaGed.Web.Filters;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// =======================================================
// MVC + Razor
// =======================================================
builder.Services.AddScoped<DatabaseSchemaExceptionFilter>();

var mvc = builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<DatabaseSchemaExceptionFilter>();
});
#if DEBUG
mvc.AddRazorRuntimeCompilation();
#endif

builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IDateTimeDisplayService, DateTimeDisplayService>();
builder.Services.AddSingleton<ISecretMasker, SecretMasker>();
builder.Services.AddSingleton<IStartupConfigurationValidator, StartupConfigurationValidator>();
builder.Services.AddSingleton<IExecutableResolver, ExecutableResolver>();
builder.Services.AddSingleton<IApplicationClock, SystemApplicationClock>();
builder.Services.AddScoped<ITenantTimeZoneProvider, ConfigurationTenantTimeZoneProvider>();
builder.Services.AddScoped<IDateTimeZoneConverter, TenantDateTimeZoneConverter>();
builder.Services.AddScoped<ITenantCatalog, DatabaseTenantCatalog>();
builder.Services.AddScoped<ISystemUserProvider, ConfiguredSystemUserProvider>();
builder.Services.AddScoped<IJobExecutionLock, PostgresJobExecutionLock>();
builder.Services.Configure<SchemaRepairOptions>(builder.Configuration.GetSection("SchemaRepair"));
builder.Services.Configure<OcrAutoScheduleOptions>(builder.Configuration.GetSection("OcrAutoSchedule"));
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection("Ocr"));
builder.Services.AddScoped<ISchemaFixSqlProvider, SchemaFixSqlProvider>();
builder.Services.AddScoped<ISchemaHealthService, SchemaHealthService>();
builder.Services.AddScoped<IHomologationHealthService, HomologationHealthService>();
builder.Services.AddScoped<ISchemaRepairService, SchemaRepairService>();
builder.Services.AddSingleton<ISchemaCompatibilityState, SchemaCompatibilityState>();
builder.Services.Configure<DocumentUploadOptions>(builder.Configuration.GetSection("DocumentUpload"));
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

// =======================================================
// DataProtection persistente para evitar logout após recycle do app pool
// =======================================================
var keysPath = builder.Configuration.GetValue<string>("DataProtection:KeysPath")
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("InovaGed");

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// =======================================================
// Current User (Tenant / User)
// =======================================================
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ICurrentContext, CurrentContext>();
builder.Services.AddScoped<IGedDashboardService, GedDashboardService>();
builder.Services.AddScoped<IHospitalOcrAnalyticsService, HospitalOcrAnalyticsService>();
builder.Services.AddScoped<IHospitalIntelligenceService, HospitalIntelligenceService>();
builder.Services.AddScoped<IHospitalTrendsService, HospitalTrendsService>();
builder.Services.AddScoped<ITableSchemaGuard, TableSchemaGuard>();
builder.Services.AddScoped<IOperationsDashboardService, OperationsDashboardService>();

builder.Services.Configure<DocumentQualityOptions>(builder.Configuration.GetSection("DocumentQuality"));
builder.Services.AddScoped<IDocumentQualityAnalyzerService, DocumentQualityAnalyzerService>();
builder.Services.AddHostedService<DocumentQualitySchedulerWorker>();

// =======================================================
// Database (PostgreSQL)
// =======================================================
builder.Services.AddSingleton<IDbConnectionFactory>(_ =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionString 'DefaultConnection' não configurada.");
    return new NpgsqlConnectionFactory(cs);
});

// =======================================================
// Storage / Preview
// =======================================================
builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection("Storage:Local"));
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();

// =======================================================
// Preview / LibreOffice
// =======================================================
builder.Services.Configure<LibreOfficeOptions>(
    builder.Configuration.GetSection("Preview"));

builder.Services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();
builder.Services.AddScoped<IPreviewStatusRepository, PreviewStatusRepository>();

builder.Services.AddSingleton<PreviewQueue>();
builder.Services.AddSingleton<IPreviewJobQueue>(sp => sp.GetRequiredService<PreviewQueue>());
builder.Services.AddScoped<IPreviewNotificationService, SignalRPreviewNotificationService>();
builder.Services.AddHostedService<PreviewWorker>();

builder.Services.Configure<StorageLocalOptions>(builder.Configuration.GetSection("Storage:Local"));

// =======================================================
// OCR / Preview pipeline
// =======================================================
builder.Services.AddScoped<IOcrProcessRunner, OcrProcessRunner>();
builder.Services.AddScoped<IOcrEnvironmentValidator, OcrEnvironmentValidator>();
builder.Services.AddScoped<IOcrService, OcrMyPdfOcrService>();
builder.Services.AddScoped<IPdfTextExtractor, PopplerPdfTextExtractor>();

builder.Services.AddScoped<IDocumentClassifier, RuleBasedDocumentClassifier>();
builder.Services.AddScoped<IDocumentClassificationRepository, DocumentClassificationRepository>();
builder.Services.AddScoped<IDocumentTypeQueries, DocumentTypeQueries>();
builder.Services.AddScoped<IDocumentClassificationQueries, DocumentClassificationQueries>();
builder.Services.AddScoped<DocumentClassificationAppService>();

builder.Services.AddScoped<IDocumentCommands, DocumentCommands>();
builder.Services.AddScoped<IDocumentTypeCatalogQueries, DocumentTypeCatalogQueries>();
builder.Services.AddScoped<IFolderClassificationRuleRepository, FolderClassificationRuleRepository>();
builder.Services.AddScoped<IOcrTextProvider, DbOcrTextProvider>();
builder.Services.AddScoped<IOcrAutoClassificationService, OcrAutoClassificationService>();
builder.Services.AddScoped<IDocumentClassificationCommands, DocumentClassificationCommands>();

builder.Services.AddScoped<IClassificationPendingCounter, ClassificationPendingCounter>();
builder.Services.AddScoped<IClassificationDashboardQueries, ClassificationDashboardQueries>();
builder.Services.AddScoped<IDocumentClassificationAuditQueries, DocumentClassificationAuditQueries>();

builder.Services.AddScoped<SimpleTextDocumentTypeSuggester>();
builder.Services.AddScoped<HybridDocumentTypeSuggester>();

// =======================================================
// PACS
// =======================================================
builder.Services.Configure<PacsIntegrationOptions>(builder.Configuration.GetSection("PacsIntegration"));
builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<IOcrQueue, OcrQueue>();
builder.Services.AddScoped<PacsIntegrationService>();

// =======================================================
// Seed
// =======================================================
builder.Services.Configure<InovaGed.Infrastructure.Setup.SystemSeedOptions>(builder.Configuration.GetSection("SystemSeed"));
builder.Services.AddHostedService<InovaGed.Infrastructure.Setup.SystemSeedHostedService>();

// =======================================================
// Classification Plan
// =======================================================
builder.Services.AddScoped<InovaGed.Application.ClassificationPlans.IClassificationPlanRepository, ClassificationPlanRepository>();
builder.Services.AddScoped<IClassificationPlanCommands, ClassificationPlanCommands>();
builder.Services.AddScoped<IClassificationPlanQueries, ClassificationPlanQueries>();

// =======================================================
// Search
// =======================================================
builder.Services.AddScoped<IDocumentSearchQueries, DocumentSearchQueries>();
builder.Services.AddScoped<IDocumentSearchTextQueries, DocumentSearchTextQueries>();
builder.Services.AddScoped<IDocumentMoveService, DocumentMoveService>();
builder.Services.AddScoped<IDocumentBulkUploadService, DocumentBulkUploadService>();
builder.Services.AddScoped<IGedBulkDocumentActionService, GedBulkDocumentActionService>();
builder.Services.AddScoped<IDocumentPartialService, DocumentPartialService>();
builder.Services.AddScoped<IUploadFolderResolver, UploadFolderResolver>();
builder.Services.AddScoped<IGedFolderMoveService, GedFolderMoveService>();
builder.Services.AddSingleton<IUploadConcurrencyLimiter, UploadConcurrencyLimiter>();
builder.Services.AddScoped<IGedProcessingJobRepository, GedProcessingJobRepository>();
builder.Services.AddScoped<IUploadBatchService, UploadBatchService>();
builder.Services.AddScoped<IUploadBatchConsistencyService, UploadBatchConsistencyService>();
builder.Services.AddScoped<IUploadChunkService, UploadChunkService>();
builder.Services.AddHostedService<StaleUploadBatchItemWorker>();
builder.Services.AddHostedService<GedProcessingWorker>();
builder.Services.AddScoped<IGedAccessPolicyService, GedAccessPolicyService>();
builder.Services.AddScoped<IGedSearchService, GedSearchService>();
builder.Services.AddScoped<IGedSmartSearchService, GedSmartSearchService>();
builder.Services.AddScoped<ISmartSearchContextParser, SmartSearchContextParser>();
builder.Services.AddScoped<ISmartQueryParser, SmartQueryParser>();
builder.Services.AddScoped<IGedSmartQueryParser, SmartQueryParser>();
builder.Services.AddScoped<IDocumentOcrMetadataExtractor, DocumentOcrMetadataExtractor>();
builder.Services.AddScoped<IGedOcrMetadataExtractor, DocumentOcrMetadataExtractor>();
builder.Services.AddScoped<ISmartSearchRepository, SmartSearchRepository>();
builder.Services.AddScoped<IGedSmartSearchRepository, SmartSearchRepository>();
builder.Services.AddScoped<ISmartSearchService, SmartSearchService>();
builder.Services.AddScoped<IDocumentChatService, DocumentChatService>();
builder.Services.AddScoped<ISearchStatisticsService, SearchStatisticsService>();
builder.Services.AddScoped<IGedSearchSuggestionService, GedSearchSuggestionService>();
builder.Services.AddScoped<IGedSearchStatisticsService, GedSearchStatisticsService>();
builder.Services.AddScoped<IGedSearchIndexService, GedSearchIndexService>();
builder.Services.AddScoped<IGedSmartSearchDiagnosticsService, GedSmartSearchDiagnosticsService>();
builder.Services.AddScoped<IGedClassificationSuggestionService, GedClassificationSuggestionService>();

// =======================================================
// Auth Repository
// =======================================================
builder.Services.AddScoped<IAuthRepository, AuthRepository>();
builder.Services.AddScoped<ICodeGeneratorService, CodeGeneratorService>();

// =======================================================
// GED – Queries
// =======================================================
builder.Services.AddScoped<IFolderQueries, FolderQueries>();
builder.Services.AddScoped<IFolderNavigationResolver, FolderNavigationResolver>();
builder.Services.AddScoped<IDocumentQueries, DocumentQueries>();
builder.Services.AddScoped<IDocumentWorkflowQueries, DocumentWorkflowQueries>();
builder.Services.AddScoped<IWorkflowQueries, WorkflowQueries>();

// =======================================================
// GED – Commands
// =======================================================
builder.Services.AddScoped<IFolderCommands, FolderCommands>();
builder.Services.AddScoped<IDocumentWorkflowCommands, DocumentWorkflowCommands>();
builder.Services.AddScoped<IWorkflowCommands, WorkflowCommands>();

// =======================================================
// OCR Jobs + Worker
// =======================================================
builder.Services.AddScoped<IOcrJobRepository, OcrJobRepository>();
builder.Services.AddScoped<IOcrAutoScheduleRepository, OcrAutoScheduleRepository>();
builder.Services.AddScoped<IOcrAutoSchedulerService, OcrAutoSchedulerService>();
builder.Services.AddScoped<OcrDashboardService>();
builder.Services.AddScoped<IOcrDashboardService>(sp => sp.GetRequiredService<OcrDashboardService>());
builder.Services.AddScoped<IOcrStatusResolver>(sp => sp.GetRequiredService<OcrDashboardService>());
builder.Services.AddHostedService<OcrAutoSchedulerWorker>();
if (builder.Configuration.GetValue<bool>("OcrWorker:Enabled"))
{
    builder.Services.AddHostedService<OcrWorker>();
}

// =======================================================
// Document Write + AuditLog (não confundir com IAuditWriter)
// =======================================================
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessorAdapter>();
builder.Services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddScoped<IOcrStatusQueries, OcrStatusQueries>();
builder.Services.AddScoped<IOcrSignalRNotifier, OcrSignalRNotifier>();

// =======================================================
// Retention (Dashboard + Queue + Worker diário)
// =======================================================
builder.Services.AddScoped<IRetentionJobRepository, RetentionJobRepository>();
builder.Services.AddScoped<RetentionRecalcService>();
builder.Services.AddScoped<IRetentionRecalcService, RetentionRecalcService>();

builder.Services.AddScoped<IRetentionQueueQueries, RetentionQueueQueries>();
builder.Services.AddScoped<IRetentionAuditWriter, RetentionAuditWriter>();
builder.Services.AddScoped<IRetentionQueueRepository, RetentionQueueRepository>();
builder.Services.AddScoped<IRetentionQueueJob, RetentionQueueJob>();
builder.Services.AddHostedService<RetentionDailyWorker>();

// =======================================================
// Retention Cases
// =======================================================
builder.Services.AddScoped<IRetentionCaseRepository, RetentionCaseRepository>();
builder.Services.AddScoped<IRetentionCaseExecutionRepository, RetentionCaseExecutionRepository>();
builder.Services.AddScoped<RetentionCaseExecutionService>();

// =======================================================
// Retention Terms
// =======================================================
builder.Services.AddScoped<IRetentionTermRepository, RetentionTermRepository>();
builder.Services.AddScoped<ITermPdfGenerator, LibreOfficeTermPdfGenerator>();

// =======================================================
// Reports / Signatures
// =======================================================
builder.Services.AddScoped<IDispositionReportsQueries, DispositionReportsQueries>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISignatureProvider, InternalSignatureProvider>();

// =======================================================
// Users / Permissions / Audit
// =======================================================
builder.Services.AddScoped<IUserAdminRepository, UserAdminRepository>();
builder.Services.AddScoped<IUserAdminQueries, UserAdminQueries>();
builder.Services.AddScoped<UserService>();

builder.Services.AddScoped<IPermissionChecker, AllowAllPermissionChecker>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<IPermissionService>(sp => sp.GetRequiredService<PermissionService>());
builder.Services.AddScoped<IParameterRepository, ParameterRepository>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();     // ✅ uma vez só
builder.Services.AddScoped<IAppAuditLogService, AppAuditLogService>();
builder.Services.AddScoped<ISystemLogQueryService, SystemLogQueryService>();
builder.Services.AddScoped<IAuditQueries, AuditQueries>();
builder.Services.AddScoped<IAuditSecurityService, AuditSecurityService>();
builder.Services.AddScoped<IAbacAuthorizationService, AbacAuthorizationService>();
builder.Services.AddScoped<ISensitiveDocumentCryptoService, SensitiveDocumentCryptoService>();
builder.Services.AddScoped<IGedIntelligenceService, GedIntelligenceService>();
builder.Services.AddScoped<IGedAdministrativeIntelligenceService, GedAdministrativeIntelligenceService>();


// =======================================================
// Loans / Batches / Physical / POP / Instruments
// =======================================================
builder.Services.AddScoped<ILoanQueries, LoanQueries>();
builder.Services.AddScoped<ILoanCommands, LoanCommands>();
builder.Services.AddScoped<ILoanHistoryWriter, LoanHistoryWriter>();
builder.Services.AddScoped<ILoanRequestService, LoanRequestService>();
builder.Services.AddScoped<IProtocolRequestService, ProtocolRequestService>();
builder.Services.AddScoped<InovaGed.Application.Ged.Protocols.IProtocolAccessService, InovaGed.Infrastructure.Ged.Protocols.ProtocolAccessService>();
builder.Services.AddScoped<IProtocolHistoryWriter, ProtocolHistoryWriter>();
builder.Services.AddScoped<ILoanAccessService, LoanAccessService>();
builder.Services.AddScoped<InovaGed.Application.Ged.Loans.IProtocolAccessService, InovaGed.Infrastructure.Ged.Loans.ProtocolAccessService>();
builder.Services.AddScoped<ISolicitacaoService, SolicitacaoService>();
builder.Services.AddScoped<ISecureDocumentLinkService, SecureDocumentLinkService>();

builder.Services.AddScoped<IBatchQueries, BatchQueries>();
builder.Services.AddScoped<IBatchCommands, BatchCommands>();

builder.Services.AddScoped<IPhysicalQueries, PhysicalQueries>();
builder.Services.AddScoped<IPhysicalCommands, PhysicalCommands>();

builder.Services.AddScoped<IPopProcedureCommands, PopProcedureCommands>();
builder.Services.AddScoped<IPopProcedureQueries, PopProcedureQueries>();

builder.Services.AddScoped<RetentionRecalculateService>();        // <-- ESTA LINHA resolve o erro
builder.Services.AddScoped<IRetentionQueueRepository, RetentionQueueRepository>();
builder.Services.AddScoped<IRetentionCaseRepository, RetentionCaseRepository>();
builder.Services.AddScoped<IRetentionQueueJob, RetentionQueueJob>();
builder.Services.AddScoped<InstrumentRepository>();

builder.Services.AddScoped<ICertificateValidationService, InovaGed.Web.Common.CertificateValidationStub>();

builder.Services.AddScoped<ICertificateValidationService, CertificateValidationService>();

builder.Services.AddScoped<ITenantAccessor, TenantAccessor>();

// Tenant provider (scoped)
builder.Services.AddScoped<ITenantProvider, HttpTenantProvider>();
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();

// Authorization middleware result handler (singleton)
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessFailureAuditHandler>();


builder.Services.AddScoped<IAccessFailureLogger, AccessFailureLogger>();
builder.Services.AddScoped<IMenuVisibilityService, MenuVisibilityService>();
// =======================================================
// ✅ LoanOverdueWorker (corrigido: Options + feature-flag)
// =======================================================
builder.Services.Configure<LoanOverdueWorkerOptions>(builder.Configuration.GetSection("Workers:LoanOverdue"));
if (builder.Configuration.GetValue<bool>("Workers:LoanOverdue:Enabled"))
{
    builder.Services.AddHostedService<LoanOverdueWorker>();
}

// =======================================================
// Authorization Policies
// =======================================================
builder.Services.AddAuthorization(options =>
{
    static void RequireAny(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder p, params string[] roles)
        => p.RequireAssertion(ctx => ctx.User.IsFullAdmin() || roles.Any(role => ctx.User.IsInNormalizedRole(role)));

    var fullAdmin = AppRoleGroups.FullAdmins;
    var gedAccess = AppRoleGroups.GedUsers;
    var hospitalDocumentsAccess = AppRoleGroups.HospitalDocumentUsers;
    var loansView = new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir };
    var loansManage = new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir };
    var loansRequest = new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.ArquivistaOphir };
    var protocolRequest = new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.ArquivistaOphir };
    var protocolView = new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir };
    var protocolManage = new[] { AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir };
    var protocolAdmin = new[] { AppRoles.Admin, AppRoles.Administrador };

    options.AddPolicy(AppPolicies.FullAdminOnly, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.SystemAdmin, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.SystemHealth, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.ParametersAdmin, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.UsersAdmin, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.UsersGlobalManage, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.UsersSectorManage, p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir));
    options.AddPolicy(AppPolicies.SystemLogs, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.SchemaRepair, p => RequireAny(p, fullAdmin));

    options.AddPolicy(AppPolicies.GedAccess, p => RequireAny(p, gedAccess));
    options.AddPolicy(AppPolicies.Ocr, p => RequireAny(p, gedAccess));
    options.AddPolicy(AppPolicies.HospitalDocumentsAccess, p => RequireAny(p, hospitalDocumentsAccess));
    options.AddPolicy(AppPolicies.LoansView, p => RequireAny(p, loansView));
    options.AddPolicy(AppPolicies.LoansManage, p => RequireAny(p, loansManage));
    options.AddPolicy(AppPolicies.LoansRequest, p => RequireAny(p, loansRequest));
    options.AddPolicy(AppPolicies.ProtocolRequest, p => RequireAny(p, protocolRequest));
    options.AddPolicy(AppPolicies.ProtocolView, p => RequireAny(p, protocolView));
    options.AddPolicy(AppPolicies.ProtocolManage, p => RequireAny(p, protocolManage));
    options.AddPolicy(AppPolicies.ProtocolAdmin, p => RequireAny(p, protocolAdmin));

    // Policies legadas mantidas como aliases compatíveis para controllers/views existentes.
    options.AddPolicy(AppPolicies.Dashboard,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista, AppRoles.Operador));

    options.AddPolicy(AppPolicies.Documentos,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista));

    options.AddPolicy(AppPolicies.Emprestimos,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista, AppRoles.Gestor));

    options.AddPolicy(AppPolicies.Relatorios,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista, AppRoles.Gestor, AppRoles.Auditor, AppRoles.Operador));

    options.AddPolicy(AppPolicies.Auditoria,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Gestor, AppRoles.Auditor));

    options.AddPolicy(AppPolicies.Administracao, p => RequireAny(p, fullAdmin));
    options.AddPolicy(AppPolicies.AdminOnly, p => RequireAny(p, fullAdmin));

    options.AddPolicy(AppPolicies.HospitalDocumentsOrLoansAccess,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.AdministradorOphir, AppRoles.ArquivistaOphir, AppRoles.Hospital, AppRoles.Arquivista, AppRoles.Gestor, AppRoles.Operador));

    options.AddPolicy(AppPolicies.Operations, p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador));
    options.AddPolicy(AppPolicies.OperationsAccess, p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador));

    options.AddPolicy(Policies.CanViewRetention,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista, AppRoles.Auditor));

    options.AddPolicy(Policies.CanManageRetention,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista));

    options.AddPolicy(Policies.CanSignRetention,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador, AppRoles.Arquivista));

    options.AddPolicy(Policies.CanExecuteFinal,
        p => RequireAny(p, AppRoles.Admin, AppRoles.Administrador));
});
// =======================================================
// ✅ Authentication (CORRIGIDO: apenas uma cadeia de AddAuthentication)
// =======================================================
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.AccessDeniedPath = "/Account/AccessDenied";
        opt.SlidingExpiration = true;
        opt.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddCertificate(CertificateAuthenticationDefaults.AuthenticationScheme, opt =>
    {
        opt.AllowedCertificateTypes = CertificateTypes.All;
        opt.RevocationMode = X509RevocationMode.Online;

        opt.Events = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = async ctx =>
            {
                var cert = ctx.ClientCertificate!;
                var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

                var validator = ctx.HttpContext.RequestServices.GetRequiredService<ICertificateValidationService>();
                var result = await validator.ValidateForLoginAsync(tenantId, cert, ctx.HttpContext.RequestAborted);

                if (!result.Success)
                {
                    ctx.Fail(result.Error ?? "Certificado inválido.");
                    return;
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()),
                    new Claim(ClaimTypes.Name, result.UserName ?? "Usuário"),
                    new Claim("cpf", result.Cpf ?? "")
                };

                ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ctx.Scheme.Name));
                ctx.Success();
            },
            OnAuthenticationFailed = ctx =>
            {
                ctx.Fail("Falha na autenticação por certificado.");
                return Task.CompletedTask;
            }
        };
    });

// =======================================================
// Application Services
// =======================================================
builder.Services.AddScoped<DocumentAppService>();

// =======================================================
// Build + Pipeline
// =======================================================
var app = builder.Build();

ValidateStartupConfiguration(app);
await ValidateDatabaseSchemaOnStartupAsync(app);

app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.UseHttpsRedirection();
app.UseMiddleware<SuspiciousRequestMiddleware>();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<RequestAuditMiddleware>();
app.UseMiddleware<AccessDeniedAuditMiddleware>();

app.MapHub<OcrStatusHub>(OcrStatusHub.Route);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static void ValidateStartupConfiguration(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupConfiguration");
    var checks = scope.ServiceProvider.GetRequiredService<IStartupConfigurationValidator>().Validate();
    foreach (var check in checks)
    {
        if (check.Severity == StartupConfigurationSeverity.Critical)
            logger.LogError("Configuração crítica: {Item} Status={Status} Valor={Value} Origem={Source} Ambiente={Environment} Recomendação={Recommendation}", check.Item, check.Status, check.MaskedValue, check.Source, check.Environment, check.Recommendation);
        else if (check.Severity == StartupConfigurationSeverity.Warning)
            logger.LogWarning("Configuração alerta: {Item} Status={Status} Valor={Value} Origem={Source} Ambiente={Environment} Recomendação={Recommendation}", check.Item, check.Status, check.MaskedValue, check.Source, check.Environment, check.Recommendation);
        else
            logger.LogInformation("Configuração: {Item} Status={Status} Origem={Source} Ambiente={Environment}", check.Item, check.Status, check.Source, check.Environment);
    }
    if (checks.Any(c => c.Severity == StartupConfigurationSeverity.Critical))
        throw new InvalidOperationException("Configuração obrigatória ausente ou insegura. Consulte logs StartupConfiguration e /SystemHealth/SecurityConfiguration.");
}

static async Task ValidateDatabaseSchemaOnStartupAsync(WebApplication app)
{
    var validate = app.Configuration.GetValue("Database:ValidateSchemaOnStartup", true);
    if (!validate)
        return;

    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaStartupValidation");

    try
    {
        var service = scope.ServiceProvider.GetRequiredService<ISchemaHealthService>();
        var report = await service.CheckAsync(CancellationToken.None);
        var schemaState = scope.ServiceProvider.GetRequiredService<ISchemaCompatibilityState>();
        if (report.IsHealthy)
        {
            schemaState.MarkCompatible();
            logger.LogInformation("Validação de schema concluída: banco compatível com checks críticos.");
            return;
        }

        var missingTables = string.Join(", ", report.MissingTables);
        var missingColumns = string.Join(", ", report.MissingColumns);
        var disableWorkers = app.Configuration.GetValue("Database:DisableWorkersWhenSchemaInvalid", true);
        var message = "Schema do banco desatualizado. Execute database/apply_all_required_migrations.sql antes de iniciar módulos críticos.";
        schemaState.MarkInvalid(report.MissingTables, report.MissingColumns, message, disableWorkers);
        logger.LogError("Schema do banco desatualizado. MissingTables=[{MissingTables}] MissingColumns=[{MissingColumns}] ScriptSugerido={Script}",
            missingTables,
            missingColumns,
            "database/apply_all_required_migrations.sql");

        if (disableWorkers)
            logger.LogWarning("Workers desativados temporariamente por schema inválido.");

        if (app.Configuration.GetValue("Database:FailFastOnInvalidSchema", false))
            throw new InvalidOperationException($"{message} MissingTables=[{missingTables}] MissingColumns=[{missingColumns}]");
    }
    catch (Exception ex) when (!app.Configuration.GetValue("Database:FailFastOnInvalidSchema", false))
    {
        var disableWorkers = app.Configuration.GetValue("Database:DisableWorkersWhenSchemaInvalid", true);
        scope.ServiceProvider.GetRequiredService<ISchemaCompatibilityState>()
            .MarkInvalid(Array.Empty<string>(), Array.Empty<string>(), ex.Message, disableWorkers);
        logger.LogError(ex, "Falha ou inconsistência durante validação de schema no startup. A aplicação continuará por configuração, mas telas críticas podem ser bloqueadas.");
        if (disableWorkers)
            logger.LogWarning("Workers desativados temporariamente por schema inválido.");
    }
}
