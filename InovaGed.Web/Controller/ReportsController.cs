using InovaGed.Application.Ged.Reports;
using InovaGed.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class ReportsController : Controller
{
    private readonly ILogger<ReportsController> _logger;
    private readonly ICurrentUser _user;
    private readonly IReportService _reports;

    public ReportsController(ILogger<ReportsController> logger, ICurrentUser user, IReportService reports)
    {
        _logger = logger;
        _user = user;
        _reports = reports;
    }

    [HttpGet("SignatureValidation")]
    public IActionResult SignatureValidation() => View(new ReportRunCreateVM { ReportType = "SIGNED_DOCS" });

    [HttpPost("SignatureValidation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignatureValidation(ReportRunCreateVM vm, CancellationToken ct)
    {
        try
        {
            var res = await _reports.CreateReportRunWithSignatureSnapshotAsync(_user.TenantId, _user.UserId, vm, ct);
            if (!res.IsSuccess)
            {
                TempData["Err"] = res.ErrorMessage;
                return View(vm);
            }

            TempData["Ok"] = $"Relatório registrado (RunId={res.Value}). Agora você pode gerar o PDF e anexar o RunId no rodapé.";
            return View(new ReportRunCreateVM { ReportType = vm.ReportType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reports.SignatureValidation failed");
            TempData["Err"] = "Erro ao gerar registro do relatório.";
            return View(vm);
        }
    }
}