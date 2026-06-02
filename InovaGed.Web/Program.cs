using InovaGed.Application.HospitalAnalytics;
using InovaGed.Application.HospitalIntelligence;
using InovaGed.Application.HospitalTrends;
using InovaGed.Infrastructure.HospitalAnalytics;
using InovaGed.Infrastructure.HospitalIntelligence;
using InovaGed.Infrastructure.HospitalTrends;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Notifications;
using InovaGed.Application.Common.Security;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Batches;
using InovaGed.Application.Ged.Classification;
using InovaGed.Application.Ged.Dashboard;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Ged.Folders;
using InovaGed.Application.Ged.Instruments;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Ged.Physical;
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
using InovaGed.Infrastructure.Common.Security;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged;
using InovaGed.Infrastructure.Ged.Batches;
using InovaGed.Infrastructure.Ged.Classification;
using InovaGed.Infrastructure.Ged.Dashboard;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Ged.Folders;
using InovaGed.Infrastructure.Ged.Instruments;
using InovaGed.Infrastructure.Ged.Loans;
using InovaGed.Infrastructure.Ged.Physical;
using InovaGed.Infrastructure.Ged.Reports;
using InovaGed.Infrastructure.Ged.Search;
using InovaGed.Infrastructure.Instruments;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Pacs;
using InovaGed.Infrastructure.Parameters;
using InovaGed.Infrastructure.Preview;
using InovaGed.Web.Notifications;
using InovaGed.Infrastructure.Reports;
using InovaGed.Infrastructure.Retention;
using InovaGed.Infrastructure.RetentionCases;
using InovaGed.Infrastructure.RetentionTerms;
using InovaGed.Infrastructure.Search;
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
using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using InovaGed.Application.Preview;
using InovaGed.Application.Security;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// =======================================================
// MVC + Razor
// =======================================================
var mvc = builder.Services.AddControllersWithViews();
#if DEBUG
mvc.AddRazorRuntimeCompilation();
#endif

builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.Configure<DocumentUploadOptions>(builder.Configuration.GetSection("DocumentUpload"));
var documentUploadMaxFileSizeMb = Math.Max(1, builder.Configuration.GetValue<int?>("DocumentUpload:MaxFileSizeMb") ?? 100);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = documentUploadMaxFileSizeMb * 1024L * 1024L;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = documentUploadMaxFileSizeMb * 1024L * 1024L;
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
builder.Services.AddScoped<IUploadFolderResolver, UploadFolderResolver>();
builder.Services.AddSingleton<IUploadConcurrencyLimiter, UploadConcurrencyLimiter>();
builder.Services.AddScoped<IUploadBatchService, UploadBatchService>();
builder.Services.AddHostedService<StaleUploadBatchItemWorker>();
builder.Services.AddScoped<IGedAccessPolicyService, GedAccessPolicyService>();
builder.Services.AddScoped<IGedSearchService, GedSearchService>();
builder.Services.AddScoped<IGedSmartSearchService, GedSmartSearchService>();
builder.Services.AddScoped<IGedClassificationSuggestionService, GedClassificationSuggestionService>();

// =======================================================
// Auth Repository
// =======================================================
builder.Services.AddScoped<IAuthRepository, AuthRepository>();

// =======================================================
// GED – Queries
// =======================================================
builder.Services.AddScoped<IFolderQueries, FolderQueries>();
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


// =======================================================
// Loans / Batches / Physical / POP / Instruments
// =======================================================
builder.Services.AddScoped<ILoanQueries, LoanQueries>();
builder.Services.AddScoped<ILoanCommands, LoanCommands>();
builder.Services.AddScoped<ILoanRequestService, LoanRequestService>();
builder.Services.AddScoped<ISolicitacaoService, SolicitacaoService>();

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
    // =======================================================
    // POLICIES DO MENU / PERFIS DO INOVAGED
    // =======================================================

    options.AddPolicy(AppPolicies.Dashboard,
        p => p.RequireRole(
            AppRoles.Admin,
            AppRoles.Arquivista,
            AppRoles.Operador));

    options.AddPolicy(AppPolicies.Documentos,
        p => p.RequireRole(
            AppRoles.Admin,
            AppRoles.Arquivista));

    options.AddPolicy(AppPolicies.Emprestimos,
        p => p.RequireRole(
            AppRoles.Admin,
            AppRoles.Arquivista,
            AppRoles.Gestor));

    options.AddPolicy(AppPolicies.Relatorios,
        p => p.RequireRole(
            AppRoles.Admin,
            AppRoles.Arquivista,
            AppRoles.Gestor,
            AppRoles.Auditor,
            AppRoles.Operador));

    options.AddPolicy(AppPolicies.Auditoria,
        p => p.RequireRole(
            AppRoles.Admin,
            AppRoles.Gestor,
            AppRoles.Auditor));

    options.AddPolicy(AppPolicies.Administracao,
        p => p.RequireRole(AppRoles.Admin));
    
    options.AddPolicy(AppPolicies.AdminOnly,
        p => p.RequireRole(AppRoles.Admin));

    options.AddPolicy(AppPolicies.HospitalDocumentsOrLoansAccess,
        p => p.RequireRole(
            AppRoles.Admin,
            AppRoles.AdministradorOphir,
            AppRoles.ArquivistaOphir,
            AppRoles.Arquivista,
            AppRoles.Gestor,
            AppRoles.Operador));

    // =======================================================
    // POLICIES QUE JÁ EXISTEM NO PROJETO
    // manter para não quebrar Temporalidade/Retention
    // =======================================================

    options.AddPolicy(Policies.CanViewRetention,
        p => p.RequireRole(AppRoles.Admin, AppRoles.Arquivista, AppRoles.Auditor));

    options.AddPolicy(Policies.CanManageRetention,
        p => p.RequireRole(AppRoles.Admin, AppRoles.Arquivista));

    options.AddPolicy(Policies.CanSignRetention,
        p => p.RequireRole(AppRoles.Admin, AppRoles.Arquivista));

    options.AddPolicy(Policies.CanExecuteFinal,
        p => p.RequireRole(AppRoles.Admin));
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

app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.UseHttpsRedirection();
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
