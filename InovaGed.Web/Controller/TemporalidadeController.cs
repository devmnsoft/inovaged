using InovaGed.Application.Common.Context;
using InovaGed.Application.Retention;
using InovaGed.Application.RetentionCases;
using InovaGed.Infrastructure.Retention;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Route("[controller]")]
public sealed class TemporalidadeController : Controller
{
    private readonly RetentionRecalculateService _svc;
    private readonly IRetentionQueueRepository _repo;
    private readonly IRetentionCaseRepository _cases;
    private readonly IRetentionQueueJob _job;
    private readonly ILogger<TemporalidadeController> _logger;
    private readonly ICurrentContext _ctx;

    public TemporalidadeController(
        RetentionRecalculateService svc,
        IRetentionQueueRepository repo,
        IRetentionCaseRepository cases,
        IRetentionQueueJob job,
        ILogger<TemporalidadeController> logger,
        ICurrentContext ctx)
    {
        _svc = svc;
        _repo = repo;
        _cases = cases;
        _job = job;
        _logger = logger;
        _ctx = ctx;
    }

    private Guid TenantIdOrThrow()
    {
        var tid = _ctx.TenantId;
        if (tid == Guid.Empty)
            throw new InvalidOperationException("TenantId não encontrado no contexto.");
        return tid;
    }

    private Guid UserIdOrEmpty() => _ctx.UserId; // pode ser Guid.Empty mesmo (PoC)

    // ✅ Normaliza bucket e garante default
    private static string NormalizeBucket(string? bucket)
    {
        bucket = (bucket ?? "").Trim().ToLowerInvariant();

        // ✅ ajuste aqui se o seu repo aceitar nomes diferentes
        // buckets típicos: overdue | due30 | due60 | due90 | all
        return bucket switch
        {
            "" => "overdue",
            "vencidos" => "overdue",
            "overdue" => "overdue",

            "30" => "due30",
            "due30" => "due30",
            "a_vencer_30" => "due30",

            "60" => "due60",
            "due60" => "due60",
            "a_vencer_60" => "due60",

            "90" => "due90",
            "due90" => "due90",
            "a_vencer_90" => "due90",

            "all" => "all",
            _ => "overdue"
        };
    }

    // GET /Temporalidade?bucket=overdue
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? bucket, CancellationToken ct)
    {
        var b = NormalizeBucket(bucket);
        var tenantId = TenantIdOrThrow();

        var rows = await _repo.ListQueueAsync(tenantId, b, ct);

        var vm = new TemporalidadeIndexVM
        {
            Bucket = b,
            Items = rows
        };

        return View(vm);
    }

    /// <summary>
    /// ✅ Botão manual "Recalcular agora"
    /// - Mantém o bucket atual ao voltar
    /// - Usa TenantId/UserId do contexto
    /// </summary>
    [ValidateAntiForgeryToken]
    [HttpPost("Recalculate")]
    public async Task<IActionResult> Recalculate([FromForm] string? bucket, CancellationToken ct)
    {
        var b = NormalizeBucket(bucket);

        try
        {
            var tenantId = TenantIdOrThrow();
            var userId = UserIdOrEmpty();

            var r = await _svc.ExecuteAsync(tenantId, userId, ct);

            TempData["Ok"] = r.CaseId.HasValue
                ? $"Recalcular concluído: {r.UpdatedDocs} docs atualizados. Caso gerado: {r.CaseId}. Itens: {r.CreatedItems}."
                : $"Recalcular concluído: {r.UpdatedDocs} docs atualizados. Nenhum vencido novo.";

            // ✅ volta para o bucket onde o usuário estava
            return RedirectToAction(nameof(Index), new { bucket = b });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual Recalculate failed Tenant={Tenant}", _ctx.TenantId);
            TempData["Err"] = ex.Message;
            return RedirectToAction(nameof(Index), new { bucket = b });
        }
    }

    /// <summary>
    /// ✅ Gera/atualiza a fila (se você tiver processo separado de geração)
    /// </summary>
    [HttpPost("GenerateNow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateNow([FromForm] string? bucket, CancellationToken ct)
    {
        var b = NormalizeBucket(bucket);

        try
        {
            var tenantId = TenantIdOrThrow();

            var inserted = await _job.RunAsync(
                tenantId,
                _ctx.UserId == Guid.Empty ? null : _ctx.UserId,
                _ctx.UserDisplay,
                ct);

            TempData["Ok"] = $"Fila gerada/atualizada. Novos itens inseridos: {inserted}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateNow failed Tenant={Tenant}", _ctx.TenantId);
            TempData["Err"] = "Falha ao gerar fila. Ver logs.";
        }

        return RedirectToAction(nameof(Index), new { bucket = b });
    }

    /// <summary>
    /// ✅ Cria Caso de Destinação a partir dos itens selecionados e redireciona para gerar Termo
    /// </summary>
    [HttpPost("CreateTerm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTerm(
        [FromForm] string? bucket,
        [FromForm] Guid[] selectedDocumentIds,
        CancellationToken ct)
    {
        var b = NormalizeBucket(bucket);

        if (selectedDocumentIds == null || selectedDocumentIds.Length == 0)
        {
            TempData["Err"] = "Selecione ao menos 1 item da fila.";
            return RedirectToAction(nameof(Index), new { bucket = b });
        }

        try
        {
            var tenantId = TenantIdOrThrow();
            var userId = UserIdOrEmpty();

            var req = new CreateRetentionCaseRequest
            {
                DocumentIds = selectedDocumentIds.Distinct().ToArray(),
                Title = $"Case de Destinação (bucket={b})",
                Notes = $"Gerado via Temporalidade em {DateTimeOffset.Now:dd/MM/yyyy HH:mm}."
            };

            var caseId = await _cases.CreateAsync(tenantId, userId, req, ct);

            TempData["Ok"] = "Caso criado com sucesso. Abrindo criação do termo...";

            // ✅ seu controller de termos é [Route("RetentionTerms")]
            return Redirect($"/RetentionTerms/CreateFromCase?caseId={caseId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateTerm failed Tenant={Tenant}", _ctx.TenantId);
            TempData["Err"] = "Falha ao criar Caso/Termo. Ver logs.";
            return RedirectToAction(nameof(Index), new { bucket = b });
        }
    }
}


public sealed class TemporalidadeIndexVM
{
    public string Bucket { get; set; } = "overdue";
    public IReadOnlyList<RetentionQueueRow> Items { get; set; } = Array.Empty<RetentionQueueRow>();

    // ✅ opcional: para facilitar a view (tabs)
    public bool IsOverdue => string.Equals(Bucket, "overdue", StringComparison.OrdinalIgnoreCase);
    public bool IsDue30 => string.Equals(Bucket, "due30", StringComparison.OrdinalIgnoreCase);
    public bool IsDue60 => string.Equals(Bucket, "due60", StringComparison.OrdinalIgnoreCase);
    public bool IsDue90 => string.Equals(Bucket, "due90", StringComparison.OrdinalIgnoreCase);
    public bool IsAll => string.Equals(Bucket, "all", StringComparison.OrdinalIgnoreCase);
}