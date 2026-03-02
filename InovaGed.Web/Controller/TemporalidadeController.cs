using System.Security.Claims;
using InovaGed.Application.Common.Context; // se existir; senão remova
using InovaGed.Application.Retention;
using InovaGed.Application.RetentionCases;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
public sealed class TemporalidadeController : Controller
{
    private readonly IRetentionQueueRepository _repo;
    private readonly IRetentionCaseRepository _cases;
    private readonly IRetentionQueueJob _job;
    private readonly ILogger<TemporalidadeController> _logger;
    private readonly ICurrentContext? _ctx;

    public TemporalidadeController(
        IRetentionQueueRepository repo,
          IRetentionCaseRepository cases,
        IRetentionQueueJob job,
        ILogger<TemporalidadeController> logger,
        IServiceProvider sp)
    {
        _repo = repo;
        _cases = cases;
        _job = job;
        _logger = logger;
        _ctx = sp.GetService(typeof(ICurrentContext)) as ICurrentContext;
    }

    private Guid TenantIdOrThrow()
    {
        var tid = _ctx?.TenantId ?? Guid.Empty;
        if (tid == Guid.Empty) throw new InvalidOperationException("TenantId não encontrado no _ctx.");
        return tid;
    }

    private Guid? UserId()
    {
        var uid = _ctx?.UserId;
        if (uid.HasValue && uid.Value != Guid.Empty) return uid;

        var s = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(s, out var g) ? g : null;
    }

    private string? UserName() => _ctx?.UserDisplay ?? User.Identity?.Name;

    // GET /Temporalidade?bucket=overdue
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? bucket, CancellationToken ct)
    {
        bucket ??= "overdue";
        var tenantId = TenantIdOrThrow();
        var rows = await _repo.ListQueueAsync(tenantId, bucket, ct);

        var vm = new TemporalidadeIndexVM
        {
            Bucket = bucket,
            Items = rows
        };

        return View(vm); // Views/Temporalidade/Index.cshtml
    }

    // POST /Temporalidade/GenerateNow
    [HttpPost("GenerateNow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateNow([FromForm] string bucket, CancellationToken ct)
    {
        try
        {
            var tenantId = TenantIdOrThrow();
            var inserted = await _job.RunAsync(tenantId, UserId(), UserName(), ct);
            TempData["Ok"] = $"Fila gerada. Novos itens inseridos: {inserted}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateNow failed");
            TempData["Err"] = "Falha ao gerar fila. Ver logs.";
        }

        return RedirectToAction(nameof(Index), new { bucket });
    }

    // POST /Temporalidade/CreateTerm
    // PoC: cria termo a partir de um "case" — na prática você pode:
    // 1) Agrupar documentos vencidos em um Case de Retenção (CaseId)
    // 2) Chamar RetentionTerms/CreateFromCase
    //
    // Aqui vou deixar um “hook”: você seleciona documentos, cria case e redireciona.
    [HttpPost("CreateTerm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTerm(
        [FromForm] string bucket,
        [FromForm] Guid[] selectedQueueIds,
        CancellationToken ct)
    {
        if (selectedQueueIds == null || selectedQueueIds.Length == 0)
        {
            TempData["Err"] = "Selecione ao menos 1 item da fila.";
            return RedirectToAction("Index", new { bucket });
        }

        try
        {
            // 1) cria case a partir da fila
            var title = $"Case de Destinação (bucket={bucket})";
            var notes = $"Gerado a partir do painel de temporalidade em {DateTimeOffset.Now:dd/MM/yyyy HH:mm}.";

            var caseId = await _cases.CreateFromQueueAsync(
                tenantId: _ctx.TenantId,
                userId: _ctx.UserId == Guid.Empty ? null : _ctx.UserId,
                userDisplay: _ctx.UserDisplay,
                queueIds: selectedQueueIds,
                title: title,
                notes: notes,
                ct: ct);

            TempData["Ok"] = $"Case criado com sucesso. CaseId={caseId}";

            // 2) redireciona para o módulo RetentionTerms criar o termo a partir do case
            // ✅ este endpoint você já vai ter no RetentionTerms (vamos ajustar se precisar):
            return Redirect($"/RetentionTerms/CreateFromCase?caseId={caseId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateTerm failed Tenant={Tenant}", _ctx.TenantId);
            TempData["Err"] = "Falha ao criar o Case/Termo. Ver logs.";
            return RedirectToAction("Index", new { bucket });
        }
    }
}

public sealed class TemporalidadeIndexVM
{
    public string Bucket { get; set; } = "overdue";
    public IReadOnlyList<RetentionQueueRow> Items { get; set; } = Array.Empty<RetentionQueueRow>();
}