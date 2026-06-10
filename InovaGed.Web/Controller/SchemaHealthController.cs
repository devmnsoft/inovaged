using System.Text;
using System.Text.Json;
using InovaGed.Application.Identity;
using InovaGed.Application.SystemHealth;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
[Route("SystemHealth/Schema")]
public sealed class SchemaHealthController : Controller
{
    private readonly ISchemaHealthService _schemaHealth;
    private readonly ISchemaRepairService _schemaRepair;
    private readonly ISchemaFixSqlProvider _fixSqlProvider;
    private readonly ICurrentUser _currentUser;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptions<SchemaRepairOptions> _repairOptions;

    public SchemaHealthController(
        ISchemaHealthService schemaHealth,
        ISchemaRepairService schemaRepair,
        ISchemaFixSqlProvider fixSqlProvider,
        ICurrentUser currentUser,
        IWebHostEnvironment environment,
        IOptions<SchemaRepairOptions> repairOptions)
    {
        _schemaHealth = schemaHealth;
        _schemaRepair = schemaRepair;
        _fixSqlProvider = fixSqlProvider;
        _currentUser = currentUser;
        _environment = environment;
        _repairOptions = repairOptions;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        PopulateRepairViewData();
        return View("~/Views/SystemHealth/Schema.cshtml", report);
    }

    [HttpGet("Data")]
    public async Task<IActionResult> Data(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        return Json(report);
    }

    [HttpGet("Report")]
    public async Task<IActionResult> Report(CancellationToken ct)
    {
        var report = await _schemaHealth.CheckAsync(ct);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"schema-health-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    }

    [Authorize(Policy = AppPolicies.SchemaRepair)]
    [HttpGet("FixScript")]
    public async Task<IActionResult> FixScript(CancellationToken ct)
    {
        var sql = await _schemaRepair.GenerateFixScriptAsync(ct);
        var fileName = $"inovaged_schema_fix_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql";
        return File(Encoding.UTF8.GetBytes(sql), "text/plain; charset=utf-8", fileName);
    }

    [Authorize(Policy = AppPolicies.SchemaRepair)]
    [HttpPost("ApplyFix")]
    public async Task<IActionResult> ApplyFix([FromBody] ApplySchemaFixRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CheckId))
            return BadRequest(new SchemaRepairResultDto { Success = false, Message = "checkId é obrigatório.", CorrelationId = HttpContext.TraceIdentifier });

        var result = await _schemaRepair.ApplyFixAsync(request.CheckId, request.Confirmation ?? string.Empty, _currentUser.UserId, ct);
        return Json(result);
    }

    [Authorize(Policy = AppPolicies.SchemaRepair)]
    [HttpPost("Preflight")]
    public async Task<IActionResult> Preflight([FromBody] ApplySchemaFixRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CheckId))
            return BadRequest(new SchemaFixPreflightResult { CanRun = false, Message = "checkId é obrigatório." });

        var report = await _schemaHealth.CheckAsync(ct);
        var fix = (await _fixSqlProvider.GetFixesAsync(report, ct)).FirstOrDefault(x => string.Equals(x.CheckId, request.CheckId, StringComparison.OrdinalIgnoreCase));
        if (fix is null)
            return Json(new SchemaFixPreflightResult { AlreadyApplied = true, ShouldSkip = true, Message = "Correção não encontrada para falha atual ou já aplicada." });

        var result = await _schemaRepair.ValidateFixAsync(fix, ct);
        return Json(result);
    }

    [Authorize(Policy = AppPolicies.SchemaRepair)]
    [HttpPost("ApplySafeFixes")]
    public async Task<IActionResult> ApplySafeFixes([FromBody] ApplySchemaFixRequest request, CancellationToken ct)
    {
        var result = await _schemaRepair.ApplySafeFixesAsync(request?.Confirmation ?? string.Empty, _currentUser.UserId, ct);
        return Json(result);
    }

    private void PopulateRepairViewData()
    {
        var options = _repairOptions.Value;
        var productionApplyBlocked = _environment.IsProduction() && !options.AllowApplyInProduction;
        ViewData["SchemaRepairEnabled"] = options.Enabled;
        ViewData["SchemaRepairCanApply"] = options.Enabled && !productionApplyBlocked;
        ViewData["SchemaRepairProductionBlocked"] = productionApplyBlocked;
        ViewData["SchemaRepairConfirmationText"] = options.ConfirmationText;
        ViewData["SchemaRepairRequireConfirmationText"] = options.RequireConfirmationText;
        ViewData["SchemaRepairCreateBackupRecommendation"] = options.CreateBackupRecommendation;
        ViewData["SchemaRepairEnvironment"] = _environment.EnvironmentName;
    }

    public sealed class ApplySchemaFixRequest
    {
        public string? CheckId { get; set; }
        public string? Confirmation { get; set; }
    }
}
