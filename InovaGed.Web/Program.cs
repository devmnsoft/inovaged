using InovaGed.Application;
using InovaGed.Application.Auditing;
using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Documents.Workflow;
using InovaGed.Application.Ged;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Pacs;
using InovaGed.Application.Retention;
using InovaGed.Application.Search;
using InovaGed.Application.Users;
using InovaGed.Application.Workflow;
using InovaGed.Infrastructure.Audit;
using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Auth;
using InovaGed.Infrastructure.Classification;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged;
using InovaGed.Infrastructure.Instruments;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Pacs;
using InovaGed.Infrastructure.Preview;
using InovaGed.Infrastructure.Retention;
using InovaGed.Infrastructure.Search;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Storage;
using InovaGed.Infrastructure.Users;
using InovaGed.Infrastructure.Workflow;
using InovaGed.Web.Auth;
using InovaGed.Web.Security;

using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// =======================================================
// MVC + Razor (DEV com runtime compilation)
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


// =======================================================
// Database (PostgreSQL)
// appsettings.json -> ConnectionStrings:DefaultConnection
// =======================================================
builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "ConnectionString 'DefaultConnection' não configurada.");

    return new NpgsqlConnectionFactory(cs);
});

// =======================================================
// Storage Local
// appsettings.json -> Storage:Local:RootPath
// =======================================================
builder.Services.Configure<LocalStorageOptions>(
    builder.Configuration.GetSection("Storage:Local"));

builder.Services.AddScoped<IFileStorage, LocalFileStorage>();

builder.Services.Configure<InovaGed.Infrastructure.Preview.LibreOfficeOptions>(
    builder.Configuration.GetSection("LibreOffice"));


// =======================================================
// OCR / Preview pipeline (usado pelo OcrWorker)
// =======================================================
builder.Services.AddScoped<IOcrService, OcrMyPdfOcrService>();
builder.Services.AddScoped<IPdfTextExtractor, PopplerPdfTextExtractor>();
builder.Services.AddScoped<IDocumentClassifier, RuleBasedDocumentClassifier>();
builder.Services.AddScoped<IDocumentClassificationRepository, DocumentClassificationRepository>();
builder.Services.AddScoped<IDocumentTypeQueries, DocumentTypeQueries>(); // se ainda não existir, eu te   
builder.Services.AddScoped<IDocumentClassificationQueries, DocumentClassificationQueries>();
builder.Services.AddScoped<DocumentClassificationAppService>();
builder.Services.AddScoped<IDocumentCommands, DocumentCommands>();
builder.Services.AddScoped<IDocumentTypeCatalogQueries, DocumentTypeCatalogQueries>();
builder.Services.Configure<LibreOfficeOptions>(builder.Configuration.GetSection("Preview"));
builder.Services.AddScoped<IPreviewGenerator, LibreOfficePreviewGenerator>();
builder.Services.AddScoped<IFolderClassificationRuleRepository, FolderClassificationRuleRepository>();
builder.Services.AddScoped<IOcrTextProvider, DbOcrTextProvider>();
builder.Services.AddScoped<IOcrAutoClassificationService, OcrAutoClassificationService>();
builder.Services.AddScoped<IDocumentClassificationCommands, DocumentClassificationCommands>();
builder.Services.AddScoped<IClassificationPendingCounter, ClassificationPendingCounter>();
builder.Services.AddScoped<IClassificationDashboardQueries, ClassificationDashboardQueries>();
builder.Services.AddScoped<IDocumentClassificationAuditQueries, DocumentClassificationAuditQueries>();
builder.Services.AddScoped<IUserAdminRepository, UserAdminRepository>();
builder.Services.AddScoped<IUserAdminQueries, UserAdminQueries>();
builder.Services.AddScoped<IDocumentWorkflowRepository, DocumentWorkflowRepository>();
builder.Services.AddScoped<IDocumentWorkflowService, DocumentWorkflowService>();
builder.Services.AddScoped<IPermissionChecker, AllowAllPermissionChecker>();
builder.Services.AddScoped<SimpleTextDocumentTypeSuggester>();
builder.Services.AddScoped<HybridDocumentTypeSuggester>();

builder.Services.Configure<PacsIntegrationOptions>(
    builder.Configuration.GetSection("PacsIntegration"));

builder.Services.Configure<StorageLocalOptions>(builder.Configuration.GetSection("Storage:Local"));

builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<IOcrQueue, OcrQueue>();
builder.Services.AddScoped<PacsIntegrationService>();

builder.Services.AddHostedService<InovaGed.Infrastructure.Setup.SystemSeedHostedService>();
builder.Services.AddScoped<InovaGed.Application.ClassificationPlans.IClassificationPlanRepository,
                          InovaGed.Infrastructure.ClassificationPlans.ClassificationPlanRepository>();

builder.Services.AddScoped<InovaGed.Application.RetentionCases.IRetentionCaseExecutionRepository,
                          InovaGed.Infrastructure.RetentionCases.RetentionCaseExecutionRepository>();

builder.Services.AddScoped<InovaGed.Application.RetentionCases.RetentionCaseExecutionService>();

builder.Services.AddScoped<InovaGed.Application.RetentionTerms.IRetentionTermRepository,
                          InovaGed.Infrastructure.RetentionTerms.RetentionTermRepository>();

// =======================================================
// Search
// =======================================================
builder.Services.AddScoped<IDocumentSearchQueries, DocumentSearchQueries>();

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
builder.Services.AddScoped<SimpleTextDocumentTypeSuggester>();
// =======================================================
// GED – Commands
// =======================================================
builder.Services.AddScoped<IFolderCommands, FolderCommands>();
builder.Services.AddScoped<IDocumentWorkflowCommands, DocumentWorkflowCommands>();
builder.Services.AddScoped<IWorkflowCommands, WorkflowCommands>();

builder.Services.AddScoped<IDocumentSearchTextQueries, DocumentSearchTextQueries>();


// =======================================================
// OCR Jobs + Worker (SOMENTE ESTE WORKER)
// =======================================================
builder.Services.AddScoped<IOcrJobRepository, OcrJobRepository>();
var ocrEnabled = builder.Configuration.GetValue<bool>("OcrWorker:Enabled");
if (ocrEnabled)
{
    builder.Services.AddHostedService<OcrWorker>();
}

// =======================================================
// Document Write + Audit
// =======================================================
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessorAdapter>();

builder.Services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddScoped<IOcrStatusQueries, OcrStatusQueries>();

// Retention
builder.Services.AddScoped<InovaGed.Application.Retention.IRetentionJobRepository,
                          InovaGed.Infrastructure.Retention.RetentionJobRepository>();

builder.Services.AddScoped<InovaGed.Application.Retention.RetentionRecalcService>();

builder.Services.AddHostedService<InovaGed.Infrastructure.Retention.RetentionDailyWorker>();

builder.Services.AddScoped<InovaGed.Application.Retention.IRetentionQueueQueries,
                          InovaGed.Infrastructure.Retention.RetentionQueueQueries>();

builder.Services.AddScoped<InovaGed.Application.Retention.IRetentionAuditWriter,
                          InovaGed.Infrastructure.Retention.RetentionAuditWriter>();

builder.Services.AddScoped<InovaGed.Application.RetentionCases.IRetentionCaseRepository,
                          InovaGed.Infrastructure.RetentionCases.RetentionCaseRepository>();

builder.Services.AddScoped<InovaGed.Application.Reports.IDispositionReportsQueries,
                          InovaGed.Infrastructure.Reports.DispositionReportsQueries>();

builder.Services.AddScoped<InovaGed.Application.Signatures.ISignatureProvider,
                          InovaGed.Infrastructure.Signatures.InternalSignatureProvider>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<InovaGed.Application.Common.Context.ICurrentContext,
                          InovaGed.Web.Common.Context.CurrentContext>();

builder.Services.AddScoped<InovaGed.Infrastructure.RetentionTerms.ITermPdfGenerator,
                          InovaGed.Infrastructure.RetentionTerms.LibreOfficeTermPdfGenerator>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.CanViewRetention,
        p => p.RequireRole(Roles.Admin, Roles.Archivist, Roles.Auditor));

    options.AddPolicy(Policies.CanManageRetention,
        p => p.RequireRole(Roles.Admin, Roles.Archivist));

    options.AddPolicy(Policies.CanSignRetention,
        p => p.RequireRole(Roles.Admin, Roles.Archivist));

    options.AddPolicy(Policies.CanExecuteFinal,
        p => p.RequireRole(Roles.Admin)); // só admin por padrão
});


builder.Services.AddScoped<InstrumentRepository>();
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<PermissionService>();

builder.Services.AddScoped<RetentionQueueRepository>();
builder.Services.AddScoped<RetentionQueueJob>();
builder.Services.AddScoped<InovaGed.Application.Retention.IRetentionQueueQueries, InovaGed.Infrastructure.Retention.RetentionQueueQueries>();
// Scoped (ok)
builder.Services.AddScoped<InovaGed.Application.Retention.RetentionRecalcService>();

// HostedService (sempre singleton) (ok)
builder.Services.AddHostedService<InovaGed.Infrastructure.Retention.RetentionDailyWorker>();

builder.Services.AddScoped<IRetentionRecalcService, RetentionRecalcService>();
 
builder.Services.AddHostedService<RetentionDailyWorker>();
// =======================================================
// Application Services
// =======================================================
builder.Services.AddScoped<DocumentAppService>();


// =======================================================
// Authentication / Authorization
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

builder.Services.AddAuthorization();

// =======================================================
// Build
// =======================================================
var app = builder.Build();

// =======================================================
// Pipeline
// =======================================================
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

// =======================================================
// Routes
// =======================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
