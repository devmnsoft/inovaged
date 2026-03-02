using InovaGed.Application;
using InovaGed.Application.Audit;
using InovaGed.Application.Auditing;
using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Context;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Batches;
using InovaGed.Application.Ged.Loans;
using InovaGed.Application.Ged.Physical;
using InovaGed.Application.Ged.Reports;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Pacs;
using InovaGed.Application.Reports;
using InovaGed.Application.Retention;
using InovaGed.Application.RetentionCases;
using InovaGed.Application.RetentionTerms;
using InovaGed.Application.Search;
using InovaGed.Application.Signatures;
using InovaGed.Application.Users;
using InovaGed.Application.Workflow;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Auth;
using InovaGed.Infrastructure.Classification;
using InovaGed.Infrastructure.ClassificationPlans;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged;
using InovaGed.Infrastructure.Ged.Batches;
using InovaGed.Infrastructure.Ged.Loans;
using InovaGed.Infrastructure.Ged.Physical;
using InovaGed.Infrastructure.Ged.Reports;
using InovaGed.Infrastructure.Instruments;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Pacs;
using InovaGed.Infrastructure.Preview;
using InovaGed.Infrastructure.Reports;
using InovaGed.Infrastructure.Retention;
using InovaGed.Infrastructure.RetentionCases;
using InovaGed.Infrastructure.RetentionTerms;
using InovaGed.Infrastructure.Search;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Signatures;
using InovaGed.Infrastructure.Storage;
using InovaGed.Infrastructure.Users;
using InovaGed.Infrastructure.Workflow;
using InovaGed.Web.Auth;
using InovaGed.Web.Common.Context;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// =======================================================
// MVC + Razor
// =======================================================
var mvc = builder.Services.AddControllersWithViews();
#if DEBUG
mvc.AddRazorRuntimeCompilation();
#endif

builder.Services.AddHttpContextAccessor();

// =======================================================
// Current User (Tenant / User)
// =======================================================
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ✅ ICurrentContext (TenantId / UserId / UserDisplay)
builder.Services.AddScoped<ICurrentContext, CurrentContext>();

// =======================================================
// Database (PostgreSQL)
// appsettings.json -> ConnectionStrings:DefaultConnection
// =======================================================
builder.Services.AddSingleton<IDbConnectionFactory>(_ =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionString 'DefaultConnection' não configurada.");
    return new NpgsqlConnectionFactory(cs);
});

// =======================================================
// Storage
// =======================================================
builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection("Storage:Local"));
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();

builder.Services.Configure<InovaGed.Infrastructure.Preview.LibreOfficeOptions>(builder.Configuration.GetSection("LibreOffice"));
builder.Services.Configure<LibreOfficeOptions>(builder.Configuration.GetSection("Preview"));
builder.Services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();

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

// =======================================================
// Search
// =======================================================
builder.Services.AddScoped<IDocumentSearchQueries, DocumentSearchQueries>();
builder.Services.AddScoped<IDocumentSearchTextQueries, DocumentSearchTextQueries>();

// =======================================================
// Auth
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

// =======================================================
// Retention (Dashboard + Queue + Job + Worker diário)
// =======================================================
builder.Services.AddScoped<IRetentionJobRepository, RetentionJobRepository>();
builder.Services.AddScoped<RetentionRecalcService>();
builder.Services.AddScoped<IRetentionRecalcService, RetentionRecalcService>();

builder.Services.AddScoped<IRetentionQueueQueries, RetentionQueueQueries>();
builder.Services.AddScoped<IRetentionAuditWriter, RetentionAuditWriter>();

// ✅ REPOSITORY da fila (Geração da fila)
builder.Services.AddScoped<IRetentionQueueRepository, RetentionQueueRepository>();

// ✅ JOB (Controller Temporalidade depende da INTERFACE)
builder.Services.AddScoped<IRetentionQueueJob, RetentionQueueJob>();

// Worker diário (HostedService)
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
builder.Services.AddScoped<ISignatureProvider, InternalSignatureProvider>();

// =======================================================
// Users / Permissions
// =======================================================
builder.Services.AddScoped<IUserAdminRepository, UserAdminRepository>();
builder.Services.AddScoped<IUserAdminQueries, UserAdminQueries>();

builder.Services.AddScoped<IPermissionChecker, AllowAllPermissionChecker>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<InovaGed.Infrastructure.Retention.RetentionRecalculateService>();

builder.Services.AddScoped<IAuditWriter, AuditWriter>();

builder.Services.AddScoped<ILoanQueries, LoanQueries>();
builder.Services.AddScoped<ILoanCommands, LoanCommands>();

builder.Services.AddScoped<IBatchQueries, BatchQueries>();
builder.Services.AddScoped<IBatchCommands, BatchCommands>();

builder.Services.AddScoped<IPhysicalQueries, PhysicalQueries>();
builder.Services.AddScoped<IPhysicalCommands, PhysicalCommands>();

builder.Services.AddScoped<IReportService, ReportService>();

// =======================================================
// Instruments (se você realmente usa DI por classe concreta, ok)
// =======================================================
builder.Services.AddScoped<InstrumentRepository>();

// =======================================================
// AUDITORIA (a que o RetentionQueueJob usa)
// =======================================================
builder.Services.AddScoped<IAuditWriter, AuditWriter>();

// =======================================================
// Authorization Policies
// =======================================================
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.CanViewRetention,
        p => p.RequireRole(Roles.Admin, Roles.Archivist, Roles.Auditor));

    options.AddPolicy(Policies.CanManageRetention,
        p => p.RequireRole(Roles.Admin, Roles.Archivist));

    options.AddPolicy(Policies.CanSignRetention,
        p => p.RequireRole(Roles.Admin, Roles.Archivist));

    options.AddPolicy(Policies.CanExecuteFinal,
        p => p.RequireRole(Roles.Admin));
});

// =======================================================
// Authentication
// =======================================================
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.AccessDeniedPath = "/Account/AccessDenied";
        opt.SlidingExpiration = true;
        opt.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

// =======================================================
// Application Services
// =======================================================
builder.Services.AddScoped<DocumentAppService>();

// =======================================================
// Build + Pipeline
// =======================================================
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();