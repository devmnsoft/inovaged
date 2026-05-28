using InovaGed.Application.DemoReadiness;
using InovaGed.Application.Identity;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Roles = AppRoles.Admin + ",ADMINISTRATOR")]
[Route("DemoReadiness")]
public sealed class DemoReadinessController : Controller
{
    private readonly IDemoReadinessService _service;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DemoReadinessController> _logger;

    public DemoReadinessController(
        IDemoReadinessService service,
        ICurrentUser currentUser,
        ILogger<DemoReadinessController> logger)
    {
        _service = service;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var report = await _service.RunAsync(_currentUser.TenantId, _currentUser.UserId, ct);
            return View(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar DemoReadiness.");
            return View(DemoReadinessReportDto.Empty("Não foi possível carregar a Central Executiva GED."));
        }
    }

    [HttpGet("RunChecks")]
    public async Task<IActionResult> RunChecks(CancellationToken ct)
    {
        try
        {
            var report = await _service.RunAsync(_currentUser.TenantId, _currentUser.UserId, ct);
            return Ok(new
            {
                success = true,
                data = report,
                correlationId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao executar checks DemoReadiness.");
            return StatusCode(500, new
            {
                success = false,
                message = "Não foi possível executar a verificação.",
                errorStep = "Servidor",
                errorLog = ex.Message,
                correlationId = HttpContext.TraceIdentifier
            });
        }
    }
}
