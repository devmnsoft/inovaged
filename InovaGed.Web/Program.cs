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
using InovaGed.Application.Common.Time;
using InovaGed.Application.Documents;
using InovaGed.Application.DocumentGuardian;
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
using InovaGed.Infrastructure.Common.Time;
using InovaGed.Infrastructure.Documents;
using InovaGed.Infrastructure.DocumentGuardian;
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

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
var allowInternalSelfSigned = builder.Configuration.GetValue<bool>("Auth:AllowInternalSelfSignedCertificates");
if (builder.Environment.IsProduction() && allowInternalSelfSigned)
{
    builder.Logging.AddFilter((category, level) => level >= LogLevel.Critical);
    throw new InvalidOperationException("Configuração insegura bloqueada: Auth:AllowInternalSelfSignedCertificates=true não é permitido em Production.");
}

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
builder.Services
    .AddInovaGedApplication(builder.Configuration)
    .AddInovaGedInfrastructure(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddScoped<IDateTimeDisplayService, DateTimeDisplayService>();
builder.Services.Configure<SchemaRepairOptions>(builder.Configuration.GetSection("SchemaRepair"));
builder.Services.Configure<SuspiciousRequestOptions>(builder.Configuration.GetSection("SuspiciousRequest"));
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
builder.Services.AddScoped<IHospitalOcrAnalyticsService, HospitalOcrAnalyticsService>();
builder.Services.AddScoped<IHospitalIntelligenceService, HospitalIntelligenceService>();
builder.Services.AddScoped<IHospitalTrendsService, HospitalTrendsService>();


// =======================================================
// Storage / Preview
// =======================================================

// =======================================================
// Preview / LibreOffice
// =======================================================
builder.Services.AddScoped<IPreviewNotificationService, SignalRPreviewNotificationService>();


// =======================================================
// OCR / Preview pipeline
// =======================================================





// =======================================================
// PACS
// =======================================================

// =======================================================
// Seed
// =======================================================
builder.Services.Configure<InovaGed.Infrastructure.Setup.SystemSeedOptions>(builder.Configuration.GetSection("SystemSeed"));
builder.Services.AddHostedService<InovaGed.Infrastructure.Setup.SystemSeedHostedService>();

// =======================================================
// Classification Plan
// =======================================================

// =======================================================
// Search
// =======================================================

// =======================================================
// Auth Repository
// =======================================================

// =======================================================
// GED – Queries
// =======================================================

// =======================================================
// GED – Commands
// =======================================================

// =======================================================
// OCR Jobs + Worker
// =======================================================

// =======================================================
// Document Write + AuditLog (não confundir com IAuditWriter)
// =======================================================
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessorAdapter>();
builder.Services.AddScoped<IOcrSignalRNotifier, OcrSignalRNotifier>();

// =======================================================
// Retention (Dashboard + Queue + Worker diário)
// =======================================================


// =======================================================
// Retention Cases
// =======================================================

// =======================================================
// Retention Terms
// =======================================================

// =======================================================
// Reports / Signatures
// =======================================================

// =======================================================
// Users / Permissions / Audit
// =======================================================



// =======================================================
// Loans / Batches / Physical / POP / Instruments
// =======================================================





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
