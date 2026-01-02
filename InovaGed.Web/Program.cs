using InovaGed.Application;
using InovaGed.Application.Auditing;
using InovaGed.Application.Auth;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Search;
using InovaGed.Application.Workflow;

using InovaGed.Infrastructure.Auditing;
using InovaGed.Infrastructure.Auth;
using InovaGed.Infrastructure.Classification;
using InovaGed.Infrastructure.Database;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.Ged;
using InovaGed.Infrastructure.Ocr;
using InovaGed.Infrastructure.Preview;
using InovaGed.Infrastructure.Search;
using InovaGed.Infrastructure.Storage;
using InovaGed.Infrastructure.Workflow;

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
builder.Services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddScoped<IOcrStatusQueries, OcrStatusQueries>();

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
