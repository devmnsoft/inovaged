using System.Diagnostics;
using System.Data;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
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

    public SystemHealthController(IDbConnectionFactory db, ICurrentUser currentUser, ILogger<SystemHealthController> logger, IWebHostEnvironment env)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _env = env;
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
}
