using System.Diagnostics;
using InovaGed.Application.Audit;
using InovaGed.Application.DemoReadiness;
using InovaGed.Application.Identity;
using InovaGed.Web.Models.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Roles = "ADMIN,ADMINISTRATOR")]
[Route("DemoReadiness")]
public sealed class DemoReadinessController(ICurrentUser currentUser, IDemoReadinessService service, IAuditWriter auditWriter, ILogger<DemoReadinessController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.IsInRole(AppRoles.AdministradorOphir) || User.IsInRole(AppRoles.ArquivistaOphir)) return Forbid();
        await auditWriter.WriteAsync(currentUser.TenantId, currentUser.UserId, "VIEW", "DEMO_READINESS", null, "Visualização da prontidão da demonstração", HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), new { source = "DemoReadiness" }, ct);
        return View();
    }

    [HttpGet("RunChecks")]
    public async Task<IActionResult> RunChecks(CancellationToken ct)
    {
        var cid = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var sw = Stopwatch.StartNew();
        try
        {
            var data = await service.RunAsync(currentUser.TenantId, currentUser.UserId, ct);
            sw.Stop();
            logger.LogInformation("DemoReadiness checks finished. tenantId={TenantId} userId={UserId} elapsedMs={ElapsedMs} total={Total} warnings={Warnings} errors={Errors} correlationId={CorrelationId}", currentUser.TenantId, currentUser.UserId, sw.ElapsedMilliseconds, data.TotalChecks, data.WarningCount, data.ErrorCount, cid);
            return Json(new { success = true, data, correlationId = cid });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro inesperado no DemoReadiness. correlationId={CorrelationId}", cid);
            return StatusCode(500, new { success = false, message = "Erro inesperado ao executar verificação.", correlationId = cid });
        }
    }
}
