using System.Diagnostics;
using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.SystemHealth;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public sealed class SystemHealthController : Controller
{
    private readonly IDbConnectionFactory _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SystemHealthController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly IOcrEnvironmentValidator _ocrEnvironmentValidator;
    private readonly IStartupConfigurationValidator _startupConfigurationValidator;

    public SystemHealthController(IDbConnectionFactory db, ICurrentUser currentUser, ILogger<SystemHealthController> logger, IWebHostEnvironment env, IOcrEnvironmentValidator ocrEnvironmentValidator, IStartupConfigurationValidator startupConfigurationValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _env = env;
        _ocrEnvironmentValidator = ocrEnvironmentValidator;
        _startupConfigurationValidator = startupConfigurationValidator;
    }

    [HttpGet("/SystemHealth")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var correlationId = HttpContext.TraceIdentifier;
        var uptime = DateTimeOffset.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var dbSw = Stopwatch.StartNew();
        var dbOk = false;
        long pending = 0, processing = 0, errors = 0;

        try
        {
            using var con = _db.CreateConnection();
            if (con.State != ConnectionState.Open)
            {
                con.Open();
            }
            await con.ExecuteScalarAsync<int>(new CommandDefinition("select 1", cancellationToken: ct));
            dbSw.Stop();
            dbOk = true;

            const string ocrSql = @"select
count(*) filter (where status = 'PENDING') as pending,
count(*) filter (where status = 'PROCESSING') as processing,
count(*) filter (where status = 'ERROR') as errors
from pacs.ocr_job
where tenant_id = @tenantId;";

            var row = await con.QuerySingleOrDefaultAsync(ocrSql, new { tenantId = _currentUser.TenantId });
            if (row is not null)
            {
                pending = (long)row.pending;
                processing = (long)row.processing;
                errors = (long)row.errors;
            }
        }
        catch (Exception ex)
        {
            dbSw.Stop();
            _logger.LogError(ex, "SystemHealth failed. Tenant={TenantId} User={UserId} CorrelationId={CorrelationId}", _currentUser.TenantId, _currentUser.UserId, correlationId);
        }

        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && _env.ContentRootPath.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));
        var diskFreeGb = drive is null ? (double?)null : Math.Round(drive.AvailableFreeSpace / 1024d / 1024d / 1024d, 2);

        return View(new
        {
            CorrelationId = correlationId,
            Environment = _env.EnvironmentName,
            Version = typeof(SystemHealthController).Assembly.GetName().Version?.ToString() ?? "n/a",
            Uptime = uptime.ToString("dd\\:hh\\:mm\\:ss"),
            DatabaseOk = dbOk,
            DatabaseElapsedMs = dbSw.ElapsedMilliseconds,
            OcrPending = pending,
            OcrProcessing = processing,
            OcrErrors = errors,
            DiskFreeGb = diskFreeGb
        });
    }

    [HttpGet("/SystemHealth/SecurityConfiguration")]
    public IActionResult SecurityConfiguration()
    {
        var checks = _startupConfigurationValidator.Validate();
        return View("~/Views/SystemHealth/SecurityConfiguration.cshtml", checks);
    }

    [HttpGet("/SystemHealth/Workers")]
    public async Task<IActionResult> Workers(CancellationToken ct)
    {
        var rows = new List<dynamic>();
        try
        {
            using var con = _db.CreateConnection();
            if (con.State != ConnectionState.Open) con.Open();
            var exists = await con.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from information_schema.tables where table_schema='ged' and table_name='worker_execution_state')", cancellationToken: ct));
            if (exists) rows.AddRange(await con.QueryAsync(new CommandDefinition("select worker_name, enabled, dependency, last_started_at_utc, last_success_at_utc, last_error, tenant_id, duration_ms, processed_count, next_run_at_utc, status, last_error_correlation_id from ged.worker_execution_state order by worker_name, tenant_id", cancellationToken: ct)));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Worker health indisponível."); }
        return View("~/Views/SystemHealth/Workers.cshtml", rows);
    }

    [HttpGet("/SystemHealth/OcrEnvironment")]
    public async Task<IActionResult> OcrEnvironment(CancellationToken ct)
    {
        var report = await _ocrEnvironmentValidator.ValidateAsync(ct);
        return View("~/Views/SystemHealth/OcrEnvironment.cshtml", report);
    }
}
